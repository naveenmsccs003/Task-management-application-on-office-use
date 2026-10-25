using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using TaskManagement.Api.Data;

namespace TaskManagement.Api.Members;

public sealed class InvitationTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public InvitationTokenProviderOptions()
    {
        Name = InvitationTokenProvider.ProviderName;
        TokenLifespan = TimeSpan.FromDays(3);
    }
}

/// <summary>
/// Signed, time-limited password-setup tokens for invited members (separate from the 30-minute reset tokens).
/// Bound to the security stamp, which changes when the password is set, so each link works once.
/// </summary>
public sealed class InvitationTokenProvider(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<InvitationTokenProviderOptions> options,
    ILogger<DataProtectorTokenProvider<ApplicationUser>> logger)
    : DataProtectorTokenProvider<ApplicationUser>(dataProtectionProvider, options, logger)
{
    public const string ProviderName = "Invitation";
    public const string Purpose = "SetInitialPassword";
}
