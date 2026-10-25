namespace TaskManagement.Api.Authorization;

/// <summary>Identity roles assigned through trusted processes (setup command, future role management).</summary>
public static class AppRoles
{
    public const string Administrator = "Administrator";
}

public static class AuthorizationPolicies
{
    public const string Administrator = "RequireAdministrator";
}
