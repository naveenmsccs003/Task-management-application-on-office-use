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
using TaskManagement.Api.Email;

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
            services.RemoveAll<IMemberEmailSender>();
            services.AddSingleton<IMemberEmailSender>(Emails);
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
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
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
                AssertSucceeded(await roleManager.CreateAsync(new ApplicationRole(role) { IsSystem = role == AppRoles.Administrator }));
            }
            AssertSucceeded(await users.AddToRoleAsync(user, role));
        }
        return user;
    }

    public Task<ApplicationUser> CreateAdministratorAsync(string userName = "admin") =>
        CreateUserAsync(userName, roles: AppRoles.Administrator);

    /// <summary>Creates a role granting the given permission keys (tab access must be listed explicitly).</summary>
    public async Task<ApplicationRole> CreateRoleAsync(string name, params string[] permissionKeys)
    {
        await using var scope = Services.CreateAsyncScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var role = new ApplicationRole(name);
        AssertSucceeded(await roleManager.CreateAsync(role));
        var ids = await db.Permissions.Where(p => permissionKeys.Contains(p.Key)).Select(p => p.Id).ToListAsync();
        Assert.Equal(permissionKeys.Distinct().Count(), ids.Count);
        db.RolePermissions.AddRange(ids.Select(id => new RolePermission { RoleId = role.Id, PermissionId = id }));
        await db.SaveChangesAsync();
        return role;
    }

    /// <summary>Creates a user holding a new role with exactly the given permissions.</summary>
    public async Task<ApplicationUser> CreateUserWithPermissionsAsync(string userName, params string[] permissionKeys)
    {
        var role = await CreateRoleAsync($"{userName}-role", permissionKeys);
        return await CreateUserAsync(userName, roles: role.Name!);
    }

    public async Task<T> WithDbAsync<T>(Func<ApplicationDbContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    public Task<int> PermissionIdAsync(string key) =>
        WithDbAsync(db => db.Permissions.Where(p => p.Key == key).Select(p => p.Id).SingleAsync());

    /// <summary>Logs in a fresh client as the given user.</summary>
    public async Task<ApiClient> LoginAsync(string userName, string password = DefaultPassword) =>
        await (await CreateApiClientAsync()).LoggedInAsync(userName, password);

    public async Task<ApiClient> CreateApiClientAsync()
    {
        var client = new ApiClient(this);
        await client.GetStatusAsync();
        return client;
    }

    private static void AssertSucceeded(IdentityResult result) =>
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
}
