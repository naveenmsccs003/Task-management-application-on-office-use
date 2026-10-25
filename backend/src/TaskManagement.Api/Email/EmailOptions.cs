namespace TaskManagement.Api.Email;

public enum EmailDeliveryMethod
{
    /// <summary>Emails are not sent; a warning is logged instead (never the message content).</summary>
    None,

    /// <summary>Writes .eml files to <see cref="EmailOptions.PickupDirectory"/>; intended for local development.</summary>
    PickupDirectory,

    Smtp,
}

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public EmailDeliveryMethod DeliveryMethod { get; set; } = EmailDeliveryMethod.None;

    public string From { get; set; } = "no-reply@localhost";

    public string PickupDirectory { get; set; } = "App_Data/mail";

    public string? SmtpHost { get; set; }

    public int SmtpPort { get; set; } = 587;

    public bool SmtpEnableSsl { get; set; } = true;

    public string? SmtpUserName { get; set; }

    /// <summary>Supply through user secrets or environment variables, never committed configuration.</summary>
    public string? SmtpPassword { get; set; }
}
