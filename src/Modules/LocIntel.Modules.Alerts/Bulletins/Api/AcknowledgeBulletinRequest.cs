namespace LocIntel.Modules.Alerts.Bulletins.Api;

public sealed record AcknowledgeBulletinRequest(Guid? SiteId = null, string? Note = null);
