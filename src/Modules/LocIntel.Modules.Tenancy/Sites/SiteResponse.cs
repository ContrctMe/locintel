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

public sealed record SiteResponse(
    Guid Id,
    Guid NodeId,
    string Name,
    string TimeZone,
    string Status,
    string Path,
    uint Version,
    string? AddressLine1,
    string? City,
    string? PostalCode,
    string? CountryCode,
    double? Latitude,
    double? Longitude,
    System.Text.Json.JsonElement Attributes
);
