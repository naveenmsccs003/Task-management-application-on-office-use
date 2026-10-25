using Microsoft.AspNetCore.Hosting;
using TaskManagement.Api.Tests.Infrastructure;

// Browser-test host: real API pipeline, in-memory database, one seeded user (alice).
// Captured password-reset links are printed as "RESETLINK <url>" for the test script.
var factory = new E2EFactory();
factory.UseKestrel(5028);
factory.StartServer();
await factory.InitializeAsync();
await factory.CreateUserAsync("alice", "alice@example.com", "correct horse battery");
Console.WriteLine("READY");
while (true)
{
    while (factory.Emails.Sent.TryDequeue(out var mail))
    {
        Console.WriteLine("RESETLINK " + mail.Link);
    }
    await Task.Delay(200);
}

sealed class E2EFactory : AuthApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseEnvironment("Development");
        builder.UseSetting("Frontend:BaseUrl", "http://localhost:4200");
    }
}
