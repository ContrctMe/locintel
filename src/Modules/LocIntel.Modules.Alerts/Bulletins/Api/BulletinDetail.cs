namespace LocIntel.Modules.Alerts.Bulletins.Api;

/// <summary>Acknowledgements are listed for managers only.</summary>
public sealed record BulletinDetail(
    BulletinView Bulletin,
    IReadOnlyList<AcknowledgementView>? Acknowledgements
);
