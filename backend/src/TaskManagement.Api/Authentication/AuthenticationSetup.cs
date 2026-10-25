using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaskManagement.Api.Authorization;
using TaskManagement.Api.Data;
using TaskManagement.Api.Email;

namespace TaskManagement.Api.Authentication;

public static class AuthenticationSetup
{
    public const string SessionCookieName = "tm.session";
    public const string AntiforgeryCookieName = "tm.antiforgery";
    public const string LoginRateLimitPolicy = "auth-login";
    public const string PasswordResetRateLimitPolicy = "auth-password-reset";

    public static IServiceCollection AddApplicationDatabase(this IServiceCollection services) =>
        services.AddDbContext<ApplicationDbContext>((provider, options) =>
            options.UseSqlServer(
                provider.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException(
                    "Connection string 'DefaultConnection' is not configured. Set it with user secrets or the " +
                    "ConnectionStrings__DefaultConnection environment variable.")));

    public static IServiceCollection AddApplicationAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AuthOptions>().Bind(configuration.GetSection(AuthOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<FrontendOptions>().Bind(configuration.GetSection(FrontendOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<EmailOptions>().Bind(configuration.GetSection(EmailOptions.SectionName));
        services.TryAddSingletonTimeProvider();

        services.AddIdentity<ApplicationUser, IdentityRole<Guid>>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddSignInManager<ApplicationSignInManager>()
            .AddPasswordValidator<ApplicationPasswordValidator>()
            .AddDefaultTokenProviders();

        services.AddOptions<IdentityOptions>().Configure<IOptions<AuthOptions>>((identity, auth) =>
        {
            // Length over composition: no forced character classes, which tend to produce predictable passwords.
            identity.Password.RequiredLength = auth.Value.PasswordMinimumLength;
            identity.Password.RequireDigit = false;
            identity.Password.RequireLowercase = false;
            identity.Password.RequireUppercase = false;
            identity.Password.RequireNonAlphanumeric = false;
            identity.Password.RequiredUniqueChars = 4;

            identity.Lockout.AllowedForNewUsers = true;
            identity.Lockout.MaxFailedAccessAttempts = auth.Value.MaxFailedAccessAttempts;
            identity.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(auth.Value.LockoutMinutes);

            identity.User.RequireUniqueEmail = true;
            // '@' is excluded so a login identifier is unambiguously a user name or an email.
            identity.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._";
            identity.SignIn.RequireConfirmedAccount = false;
        });

        services.AddOptions<SecurityStampValidatorOptions>().Configure<IOptions<AuthOptions>>((validator, auth) =>
            validator.ValidationInterval = TimeSpan.FromMinutes(auth.Value.SecurityStampValidationMinutes));

        services.AddOptions<DataProtectionTokenProviderOptions>().Configure<IOptions<AuthOptions>>((tokens, auth) =>
            tokens.TokenLifespan = TimeSpan.FromMinutes(auth.Value.PasswordResetTokenLifetimeMinutes));

        services.AddSingleton<DbTicketStore>();
        services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
            .Configure<IHostEnvironment, DbTicketStore, IOptions<AuthOptions>>((cookie, environment, store, auth) =>
            {
                cookie.Cookie.Name = SessionCookieName;
                cookie.Cookie.HttpOnly = true;
                cookie.Cookie.SameSite = SameSiteMode.Strict;
                cookie.Cookie.SecurePolicy = environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
                cookie.SlidingExpiration = true;
                // Per-session expiry is set at sign-in (Remember Me or not); this is only the fallback.
                cookie.ExpireTimeSpan = TimeSpan.FromMinutes(auth.Value.SessionIdleTimeoutMinutes);
                cookie.SessionStore = store;
                cookie.Events.OnRedirectToLogin = context => WriteStatusAsync(context.HttpContext, StatusCodes.Status401Unauthorized);
                cookie.Events.OnRedirectToAccessDenied = context => WriteStatusAsync(context.HttpContext, StatusCodes.Status403Forbidden);
            });
        services.AddHostedService<SessionCleanupService>();

        services.AddAntiforgery(options =>
        {
            options.HeaderName = AntiforgeryCookie.HeaderName;
            options.Cookie.Name = AntiforgeryCookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        });

        services.AddAuthorizationBuilder()
            // Every endpoint requires an authenticated user unless it explicitly opts out with [AllowAnonymous].
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(AuthorizationPolicies.Administrator, policy => policy.RequireRole(AppRoles.Administrator));
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddScoped<IPermissionService, DenyAllPermissionService>();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, _) => new ValueTask(WriteStatusAsync(context.HttpContext, StatusCodes.Status429TooManyRequests,
                "Too many attempts. Please wait a moment and try again."));
            options.AddPolicy(LoginRateLimitPolicy, context => PerClientWindow(context,
                auth => (auth.LoginRateLimitPermits, auth.LoginRateLimitWindowSeconds)));
            options.AddPolicy(PasswordResetRateLimitPolicy, context => PerClientWindow(context,
                auth => (auth.PasswordResetRateLimitPermits, auth.PasswordResetRateLimitWindowSeconds)));
        });

        services.AddScoped<AuthenticationService>();
        services.AddScoped<Setup.AdminProvisioner>();
        services.AddTransient<IEmailSender<ApplicationUser>, SmtpEmailSender>();
        return services;
    }

    private static void TryAddSingletonTimeProvider(this IServiceCollection services)
    {
        if (!services.Any(d => d.ServiceType == typeof(TimeProvider)))
        {
            services.AddSingleton(TimeProvider.System);
        }
    }

    private static RateLimitPartition<string> PerClientWindow(HttpContext context, Func<AuthOptions, (int Permits, int WindowSeconds)> select)
    {
        var (permits, windowSeconds) = select(context.RequestServices.GetRequiredService<IOptions<AuthOptions>>().Value);
        // Behind a reverse proxy, configure forwarded headers so RemoteIpAddress is the real client.
        var partitionKey = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permits,
            Window = TimeSpan.FromSeconds(windowSeconds),
            QueueLimit = 0,
        });
    }

    private static async Task WriteStatusAsync(HttpContext context, int statusCode, string? detail = null)
    {
        context.Response.StatusCode = statusCode;
        var problemDetails = context.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problemDetails.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails { Status = statusCode, Detail = detail },
        });
    }
}
