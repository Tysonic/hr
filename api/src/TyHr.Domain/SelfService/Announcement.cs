namespace TyHr.Domain.SelfService;

/// <summary>A notice from HR to everyone in the company, shown on the home page until it expires.</summary>
public class Announcement : TenantEntity
{
    private Announcement() { }

    public Announcement(string title, string body, bool pinned, DateOnly? expiresOn, string publishedBy, DateTimeOffset at, DateOnly today)
    {
        Update(title, body, pinned, expiresOn, today);
        PublishedBy = publishedBy;
        PublishedAt = at;
    }

    public string Title { get; private set; } = null!;
    public string Body { get; private set; } = null!;
    public bool Pinned { get; private set; }

    /// <summary>The last day it's shown; null to show it until removed.</summary>
    public DateOnly? ExpiresOn { get; private set; }

    public string PublishedBy { get; private set; } = null!;
    public DateTimeOffset PublishedAt { get; private set; }

    public void Update(string title, string body, bool pinned, DateOnly? expiresOn, DateOnly today)
    {
        Title = Guard.Required(title, "Title");
        if (Title.Length > 200) throw new DomainException("Keep the title under 200 characters.");
        Body = Guard.Required(body, "Message");
        if (Body.Length > 10_000) throw new DomainException("Keep the message under 10,000 characters.");
        if (expiresOn < today) throw new DomainException("The end date has passed; pick today or later.");
        Pinned = pinned;
        ExpiresOn = expiresOn;
    }

    public bool IsCurrentOn(DateOnly today) => ExpiresOn == null || ExpiresOn >= today;
}
