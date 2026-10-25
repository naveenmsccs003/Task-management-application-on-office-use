using Microsoft.EntityFrameworkCore;
using TaskManagement.Api.Data;

namespace TaskManagement.Api.Setup;

/// <summary>
/// Operator-only provisioning of the first administrator, run on the server:
/// <c>dotnet TaskManagement.Api.dll seed-admin --username admin --email admin@example.com [--display-name "Admin"]</c>.
/// The password is read from the TM_ADMIN_PASSWORD environment variable or prompted for (hidden input).
/// There is no HTTP endpoint for this, and the command refuses to run once an administrator exists.
/// </summary>
public static class AdminSetupCommand
{
    public const string CommandName = "seed-admin";
    public const string GrantCommandName = "grant-admin";
    public const string PasswordEnvironmentVariable = "TM_ADMIN_PASSWORD";

    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        var userName = GetOption(args, "--username");
        var email = GetOption(args, "--email");
        var displayName = GetOption(args, "--display-name") ?? userName;
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(email))
        {
            Console.Error.WriteLine($"Usage: {CommandName} --username <name> --email <address> [--display-name <name>]");
            return 2;
        }

        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var db = provider.GetRequiredService<ApplicationDbContext>();
        if ((await db.Database.GetPendingMigrationsAsync()).Any())
        {
            Console.Error.WriteLine("The database has pending migrations. Apply them first (dotnet ef database update).");
            return 1;
        }

        var provisioner = provider.GetRequiredService<AdminProvisioner>();
        if (await provisioner.IsSetupCompletedAsync())
        {
            Console.Error.WriteLine(AdminProvisioner.AlreadyCompletedMessage);
            return 1;
        }

        var password = Environment.GetEnvironmentVariable(PasswordEnvironmentVariable);
        if (string.IsNullOrEmpty(password))
        {
            password = ReadHiddenLine("Administrator password: ");
            if (password != ReadHiddenLine("Confirm password: "))
            {
                Console.Error.WriteLine("Passwords do not match.");
                return 1;
            }
        }

        var result = await provisioner.CreateInitialAdministratorAsync(userName, email, displayName, password);
        foreach (var error in result.Errors)
        {
            Console.Error.WriteLine(error);
        }
        if (!result.Succeeded)
        {
            return 1;
        }

        Console.WriteLine($"Administrator '{userName}' created (id {result.User!.Id}).");
        return 0;
    }

    /// <summary>
    /// Recovery: <c>grant-admin --email user@example.com</c> restores administrator access to an existing account,
    /// only when no active administrator exists.
    /// </summary>
    public static async Task<int> RunGrantAsync(IServiceProvider services, string[] args)
    {
        var email = GetOption(args, "--email");
        if (string.IsNullOrWhiteSpace(email))
        {
            Console.Error.WriteLine($"Usage: {GrantCommandName} --email <address>");
            return 2;
        }

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if ((await db.Database.GetPendingMigrationsAsync()).Any())
        {
            Console.Error.WriteLine("The database has pending migrations. Apply them first (dotnet ef database update).");
            return 1;
        }

        var result = await scope.ServiceProvider.GetRequiredService<AdminProvisioner>().GrantAdministratorAsync(email);
        foreach (var error in result.Errors)
        {
            Console.Error.WriteLine(error);
        }
        if (!result.Succeeded)
        {
            return 1;
        }
        Console.WriteLine($"Administrator access restored for '{result.User!.UserName}'.");
        return 0;
    }

    private static string? GetOption(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static string ReadHiddenLine(string prompt)
    {
        Console.Write(prompt);
        if (Console.IsInputRedirected)
        {
            return Console.ReadLine() ?? string.Empty;
        }

        var buffer = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return buffer.ToString();
            }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0)
                {
                    buffer.Length--;
                }
            }
            else if (!char.IsControl(key.KeyChar))
            {
                buffer.Append(key.KeyChar);
            }
        }
    }
}
