using LocIntel.Contracts;
using LocIntel.Modules.Tenancy.Data;
using LocIntel.Modules.Tenancy.Hierarchy;
using LocIntel.Platform.Data;
using LocIntel.Platform.Entitlements;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Messaging;
using LocIntel.Platform.Spatial;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Tenancy.Sites;

/// <summary>
/// Patch semantics: null = unchanged. Address fields accept "" to CLEAR -
/// an address typo must be fixable, and so must an address that never
/// existed (finding 2 of the competitive review).
/// </summary>
public sealed record UpdateSiteRequest(
    string? Name,
    string? TimeZone,
    SiteStatus? Status,
    string? AddressLine1 = null,
    string? City = null,
    string? PostalCode = null,
    string? CountryCode = null,
    double? Latitude = null,
    double? Longitude = null,
    System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement>? Attributes = null,
    uint? Version = null
);
