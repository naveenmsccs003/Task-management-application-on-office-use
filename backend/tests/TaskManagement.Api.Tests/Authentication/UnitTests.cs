using Microsoft.AspNetCore.Identity;
using TaskManagement.Api.Authentication;
using TaskManagement.Api.Data;

namespace TaskManagement.Api.Tests.Authentication;

public class DbTicketStoreTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(10, 60, false)]
    [InlineData(-1, 60, true)]
    [InlineData(10, -1, true)]
    public void Session_is_expired_when_either_sliding_or_absolute_expiry_has_passed(int slidingMinutes, int absoluteMinutes, bool expected)
    {
        var session = new UserSession
        {
            Id = "x",
            Ticket = [],
            ExpiresAtUtc = Now.AddMinutes(slidingMinutes),
            AbsoluteExpiresAtUtc = Now.AddMinutes(absoluteMinutes),
        };

        Assert.Equal(expected, DbTicketStore.IsExpired(session, Now));
    }
}

public class ApplicationPasswordValidatorTests
{
    private readonly ApplicationPasswordValidator _validator = new();
    private readonly ApplicationUser _user = new() { UserName = "jsmith", Email = "john.smith@example.com" };

    [Theory]
    [InlineData("a perfectly fine passphrase", true)]
    [InlineData("jsmith-is-my-password", false)]
    [InlineData("JOHN.SMITH rules the world", false)]
    public async Task Rejects_passwords_containing_account_identifiers(string password, bool valid)
    {
        var result = await _validator.ValidateAsync(null!, _user, password);

        Assert.Equal(valid, result.Succeeded);
    }

    [Fact]
    public async Task Rejects_passwords_longer_than_the_maximum()
    {
        var result = await _validator.ValidateAsync(null!, _user, new string('x', ApplicationPasswordValidator.MaximumLength + 1));

        Assert.Contains(result.Errors, e => e.Code == "PasswordTooLong");
    }
}
