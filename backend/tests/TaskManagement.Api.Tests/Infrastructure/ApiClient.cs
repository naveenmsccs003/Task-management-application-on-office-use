using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using TaskManagement.Api.Authentication;

namespace TaskManagement.Api.Tests.Infrastructure;

/// <summary>Browser-like client: keeps cookies and echoes the XSRF-TOKEN cookie in the X-XSRF-TOKEN header, as Angular does.</summary>
public sealed class ApiClient
{
    public static readonly Uri BaseAddress = new("https://localhost/");

    public ApiClient(AuthApiFactory factory, CookieContainer? cookies = null)
    {
        Cookies = cookies ?? new CookieContainer();
        Http = factory.CreateDefaultClient(BaseAddress, new CookieContainerHandler(Cookies));
    }

    public HttpClient Http { get; }

    public CookieContainer Cookies { get; }

    public string? SessionCookie => Cookies.GetCookies(BaseAddress)[AuthenticationSetup.SessionCookieName]?.Value;

    public async Task<AuthStatusResponse> GetStatusAsync()
    {
        var response = await Http.GetAsync("/api/auth/me");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthStatusResponse>())!;
    }

    public Task<HttpResponseMessage> PostAsync(string path, object? body = null, bool includeAntiforgeryToken = true) =>
        SendAsync(HttpMethod.Post, path, body, includeAntiforgeryToken);

    public Task<HttpResponseMessage> PutAsync(string path, object? body = null) => SendAsync(HttpMethod.Put, path, body);

    public Task<HttpResponseMessage> DeleteAsync(string path) => SendAsync(HttpMethod.Delete, path, null);

    public Task<HttpResponseMessage> GetAsync(string path) => Http.GetAsync(path);

    public async Task<T> GetJsonAsync<T>(string path)
    {
        var response = await Http.GetAsync(path);
        Assert.True(response.IsSuccessStatusCode, $"GET {path} returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions))!;
    }

    public static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new(System.Text.Json.JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, bool includeAntiforgeryToken = true)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is HttpContent content)
        {
            request.Content = content;
        }
        else if (method != HttpMethod.Delete || body is not null)
        {
            request.Content = JsonContent.Create(body ?? new { });
        }
        var token = Cookies.GetCookies(BaseAddress)[AntiforgeryCookie.RequestTokenCookieName]?.Value;
        if (includeAntiforgeryToken && token is not null)
        {
            request.Headers.Add(AntiforgeryCookie.HeaderName, token);
        }
        return Http.SendAsync(request);
    }

    public Task<HttpResponseMessage> LoginAsync(string userNameOrEmail, string password = AuthApiFactory.DefaultPassword, bool rememberMe = false) =>
        PostAsync("/api/auth/login", new { userNameOrEmail, password, rememberMe });

    public async Task<ApiClient> LoggedInAsync(string userNameOrEmail, string password = AuthApiFactory.DefaultPassword)
    {
        var response = await LoginAsync(userNameOrEmail, password);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return this;
    }

    public Task<HttpResponseMessage> LogoutAsync() => PostAsync("/api/auth/logout");
}
