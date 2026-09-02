namespace LocIntel.Modules.Marketplace.Requests.Api;

/// <summary>Guard or crew position at check-in/out; distance to the site is computed and kept.</summary>
public sealed record CheckInRequest(double Latitude, double Longitude, string? Note = null);
