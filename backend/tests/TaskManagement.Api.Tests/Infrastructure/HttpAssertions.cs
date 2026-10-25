using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace TaskManagement.Api.Tests.Infrastructure;

public static class HttpAssertions
{
    public static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    /// <summary>Returns the validation messages for a field of a ValidationProblemDetails response.</summary>
    public static async Task<string[]> ValidationErrorsAsync(this HttpResponseMessage response, string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        // Parse from the (repeatable) string so the same response can be inspected for several fields.
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
        var errors = json.GetProperty("errors");
        foreach (var property in errors.EnumerateObject())
        {
            if (string.Equals(property.Name, field, StringComparison.OrdinalIgnoreCase))
            {
                return [.. property.Value.EnumerateArray().Select(e => e.GetString()!)];
            }
        }
        Assert.Fail($"No validation errors for '{field}'. Body: {json}");
        return [];
    }
}
