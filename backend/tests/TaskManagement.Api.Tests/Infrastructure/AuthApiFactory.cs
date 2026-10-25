using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TaskManagement.Api.Authentication;
using TaskManagement.Api.Authorization;
using TaskManagement.Api.Data;

namespace TaskManagement.Api.Tests.Infrastructure;

/// <summary>
/// Hosts the real API in memory against an isolated in-memory SQLite database (one per factory).
/// SQLite stands in for SQL Server so the suite runs without a database server; the schema is
/// created from the EF model rather than the SQL Server migrations.
/// </summary>
public class AuthApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string DefaultPassword = "correct horse battery";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public CapturingEmailSender Emails { get; } = new();

    public MutableTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

    public Action<AuthOptions>? ConfigureAuth { get; init; }

    public Action<IServiceCollection>? ConfigureServices { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Frontend:BaseUrl", "https://app.test");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(_connection));

            services.RemoveAll<IEmailSender<ApplicationUser>>();
            services.AddSingleton<IEmailSender<ApplicationUser>>(Emails);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);

            services.PostConfigure<AuthOptions>(options =>
            {
                options.LoginRateLimitPermits = 1000;
                options.PasswordResetRateLimitPermits = 1000;
                ConfigureAuth?.Invoke(options);
            });

            services.AddControllers().AddApplicationPart(typeof(AuthApiFactory).Assembly);
            ConfigureServices?.Invoke(services);
        });
    }

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreatedAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _connection.DisposeAsync();
    }

    public async Task<ApplicationUser> CreateUserAsync(
        string userName, string? email = null, string password = DefaultPassword, bool isActive = true, params string[] roles)
    {
        await using var scope = Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var user = new ApplicationUser
        {
            UserName = userName,
            Email = email ?? $"{userName}@example.com",
            DisplayName = userName,
            IsActive = isActive,
        };
        AssertSucceeded(await users.CreateAsync(user, password));
        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                AssertSucceeded(await roleManager.CreateAsync(new IdentityRole<Guid>(role)));
            }
            AssertSucceeded(await users.AddToRoleAsync(user, role));
        }
        return user;
    }

    public Task<ApplicationUser> CreateAdministratorAsync(string userName = "admin") =>
        CreateUserAsync(userName, roles: AppRoles.Administrator);

    public async Task<T> WithDbAsync<T>(Func<ApplicationDbContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    public async Task<ApiClient> CreateApiClientAsync()
    {
        var client = new ApiClient(this);
        await client.GetStatusAsync();
        return client;
    }

    private static void AssertSucceeded(IdentityResult result) =>
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
}
