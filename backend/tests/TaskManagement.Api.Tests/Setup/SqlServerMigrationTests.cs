using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TaskManagement.Api.Authorization;
using TaskManagement.Api.Data;
using TaskManagement.Api.Setup;
using TaskManagement.Api.Tests.Infrastructure;

namespace TaskManagement.Api.Tests.Setup;

/// <summary>Runs only when TM_TEST_SQLSERVER holds a SQL Server connection string (the database is created and dropped).</summary>
public sealed class SqlServerFactAttribute : FactAttribute
{
    public const string Variable = "TM_TEST_SQLSERVER";

    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable)))
        {
            Skip = $"Set {Variable} to a SQL Server connection string to run the real-database test.";
        }
    }
}

public class SqlServerMigrationTests
{
    [SqlServerFact]
    public async Task Migrations_apply_to_sql_server_and_support_the_full_authentication_flow()
    {
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable(SqlServerFactAttribute.Variable))
        {
            InitialCatalog = $"TaskManagement_Test_{Guid.NewGuid():N}",
        }.ConnectionString;
        await using var factory = new SqlServerFactory(connection);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            try
            {
                await db.Database.MigrateAsync();
                Assert.Empty(await db.Database.GetPendingMigrationsAsync());

                var result = await scope.ServiceProvider.GetRequiredService<AdminProvisioner>()
                    .CreateInitialAdministratorAsync("admin", "admin@example.com", "Administrator", "first operator passphrase");
                Assert.True(result.Succeeded, string.Join("; ", result.Errors));

                var client = new ApiClient(factory);
                await client.GetStatusAsync();
                Assert.Equal(HttpStatusCode.Unauthorized, (await client.LoginAsync("admin", "wrong passphrase value")).StatusCode);
                Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync("admin@example.com", "first operator passphrase", rememberMe: true)).StatusCode);
                Assert.Contains(AppRoles.Administrator, (await client.GetStatusAsync()).User!.Roles);
                Assert.Equal(1, await db.UserSessions.CountAsync());
                Assert.Equal(HttpStatusCode.NoContent, (await client.LogoutAsync()).StatusCode);
                Assert.Equal(0, await db.UserSessions.CountAsync());
                Assert.False((await scope.ServiceProvider.GetRequiredService<AdminProvisioner>()
                    .CreateInitialAdministratorAsync("second", "second@example.com", null, "second operator passphrase")).Succeeded);
            }
            finally
            {
                await db.Database.EnsureDeletedAsync();
            }
        }
    }

    private sealed class SqlServerFactory(string connectionString) : AuthApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
                services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(connectionString));
            });
        }
    }
}
