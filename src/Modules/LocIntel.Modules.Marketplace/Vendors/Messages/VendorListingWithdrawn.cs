using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Marketplace.Vendors.Messages;

/// <summary>The profile was unpublished: the directory row goes.</summary>
public sealed record VendorListingWithdrawn(OrgId OrgId);
