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
/// WithoutCoordinates is set only for a bbox query (ADR 49): sites the
/// scope (and search) hold that can never be inside a box, so the map's
/// list can name them instead of letting them vanish.
/// </summary>
/// <summary>
/// A page of the list. <c>Next</c> is the opaque cursor for the page after
/// this one (null on the last), passed back as <c>after</c>. A search
/// stops counting at <see cref="SearchCountCap"/> and says so with
/// <c>TotalIsLowerBound</c>.
/// </summary>
public sealed record SiteListResponse(
    IReadOnlyList<SiteResponse> Items,
    int Total,
    int OpenCount,
    string? Next,
    int? WithoutCoordinates = null,
    bool TotalIsLowerBound = false
);
