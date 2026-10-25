namespace TaskManagement.Api.Authorization;

public static class AppRoles
{
    /// <summary>The protected system role. Created by the setup command; holds every active permission.</summary>
    public const string Administrator = "Administrator";
}

public static class AuthorizationPolicies
{
    public const string Administrator = "RequireAdministrator";
}
