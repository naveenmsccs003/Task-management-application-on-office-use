using System.Text.Json.Serialization;
using TaskManagement.Api.Authentication;
using TaskManagement.Api.Infrastructure;
using TaskManagement.Api.Setup;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers(options => options.Filters.Add<ValidateAntiforgeryFilter>())
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

builder.Services.AddApplicationDatabase();
builder.Services.AddApplicationAuthentication(builder.Configuration);

var app = builder.Build();

if (args.Length > 0 && args[0] == AdminSetupCommand.CommandName)
{
    return await AdminSetupCommand.RunAsync(app.Services, args[1..]);
}
if (args.Length > 0 && args[0] == AdminSetupCommand.GrantCommandName)
{
    return await AdminSetupCommand.RunGrantAsync(app.Services, args[1..]);
}

app.UseExceptionHandler();
app.UseStatusCodePages();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}
app.UseHttpsRedirection();
app.UseSecurityHeaders();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

// Serves the built Angular app from wwwroot when deployed on the same origin as the API.
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();
app.MapFallback("/api/{**path}", () => Results.Problem(statusCode: StatusCodes.Status404NotFound)).AllowAnonymous();
app.MapFallbackToFile("index.html").AllowAnonymous();

await app.RunAsync();
return 0;

public partial class Program;
