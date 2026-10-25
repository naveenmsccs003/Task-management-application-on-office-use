namespace TaskManagement.Api.Tests.Infrastructure;

/// <summary>Gives every test its own API host and database.</summary>
public abstract class ApiTestBase : IAsyncLifetime
{
    private readonly List<AuthApiFactory> _factories = [];

    protected AuthApiFactory Factory { get; private set; } = null!;

    public async Task InitializeAsync() => Factory = await StartAsync(new AuthApiFactory());

    /// <summary>Starts an additional host with custom configuration; disposed with the test.</summary>
    protected async Task<AuthApiFactory> StartAsync(AuthApiFactory factory)
    {
        _factories.Add(factory);
        await factory.InitializeAsync();
        return factory;
    }

    public async Task DisposeAsync()
    {
        foreach (var factory in _factories)
        {
            await ((IAsyncLifetime)factory).DisposeAsync();
        }
    }
}
