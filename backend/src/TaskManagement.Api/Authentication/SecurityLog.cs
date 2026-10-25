namespace TaskManagement.Api.Authentication;

/// <summary>
/// Structured security events. Never pass passwords, tokens, cookies or submitted login identifiers;
/// use the stable user id when the account is known.
/// </summary>
public static partial class SecurityLog
{
    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "Login succeeded for user {UserId} (persistent: {IsPersistent}) from {RemoteIp}.")]
    public static partial void LoginSucceeded(ILogger logger, Guid userId, bool isPersistent, string? remoteIp);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Warning, Message = "Login failed: invalid password for user {UserId} from {RemoteIp}.")]
    public static partial void LoginFailedInvalidPassword(ILogger logger, Guid userId, string? remoteIp);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Warning, Message = "Login failed: unknown account from {RemoteIp}.")]
    public static partial void LoginFailedUnknownAccount(ILogger logger, string? remoteIp);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Warning, Message = "Login rejected: account {UserId} is locked out (attempt from {RemoteIp}).")]
    public static partial void LoginLockedOut(ILogger logger, Guid userId, string? remoteIp);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Warning, Message = "Login rejected: account {UserId} is not allowed to sign in (inactive) from {RemoteIp}.")]
    public static partial void LoginNotAllowed(ILogger logger, Guid userId, string? remoteIp);

    [LoggerMessage(EventId = 1010, Level = LogLevel.Information, Message = "User {UserId} logged out.")]
    public static partial void LoggedOut(ILogger logger, Guid userId);

    [LoggerMessage(EventId = 1020, Level = LogLevel.Information, Message = "User {UserId} changed their password; {RevokedSessions} other session(s) revoked.")]
    public static partial void PasswordChanged(ILogger logger, Guid userId, int revokedSessions);

    [LoggerMessage(EventId = 1021, Level = LogLevel.Warning, Message = "Password change failed for user {UserId}: {Reason}.")]
    public static partial void PasswordChangeFailed(ILogger logger, Guid userId, string reason);

    [LoggerMessage(EventId = 1030, Level = LogLevel.Information, Message = "Password reset requested for user {UserId}.")]
    public static partial void PasswordResetRequested(ILogger logger, Guid userId);

    [LoggerMessage(EventId = 1031, Level = LogLevel.Information, Message = "Password reset requested for an unknown or inactive account from {RemoteIp}.")]
    public static partial void PasswordResetRequestedUnknown(ILogger logger, string? remoteIp);

    [LoggerMessage(EventId = 1032, Level = LogLevel.Information, Message = "Password reset completed for user {UserId}; all sessions revoked.")]
    public static partial void PasswordResetCompleted(ILogger logger, Guid userId);

    [LoggerMessage(EventId = 1033, Level = LogLevel.Warning, Message = "Password reset rejected (invalid or expired token) from {RemoteIp}.")]
    public static partial void PasswordResetRejected(ILogger logger, string? remoteIp);

    [LoggerMessage(EventId = 1034, Level = LogLevel.Error, Message = "Password reset email could not be sent for user {UserId}.")]
    public static partial void PasswordResetEmailFailed(ILogger logger, Exception exception, Guid userId);

    [LoggerMessage(EventId = 1040, Level = LogLevel.Information, Message = "Initial administrator {UserId} created by the setup command.")]
    public static partial void AdministratorCreated(ILogger logger, Guid userId);
}
