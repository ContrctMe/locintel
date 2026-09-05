using LocIntel.Platform.Entitlements;
using LocIntel.Platform.Kernel;
using Microsoft.AspNetCore.Http;

namespace LocIntel.Contracts;

/// <summary>
/// The HTTP adapter for <see cref="GateOutcome"/>: the ONE place the gates
/// become status codes. 401 for no org-bearing principal, 403 for a missing
/// grant (the documented contract - every endpoint had been answering 401),
/// and for gate 1 the single 402 body that had grown three shapes.
/// </summary>
public static class GateResults
{
    public static IResult ToResult(this GateOutcome outcome) =>
        outcome switch
        {
            GateOutcome.NotSignedIn => Results.Unauthorized(),
            GateOutcome.Forbidden f => Results.Json(
                new { error = "missing grant", capability = f.Capability },
                statusCode: StatusCodes.Status403Forbidden
            ),
            GateOutcome.Allowed => throw new InvalidOperationException(
                "ToResult is for failures; an Allowed outcome proceeds"
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
        };

    /// <summary>
    /// Gate 1 at a creation point (ADR 8/9): a limit failure is 402-and-upsell,
    /// never an error. One body shape for every limit.
    /// </summary>
    public static IResult LimitReached(EntitlementDecision decision) =>
        Results.Json(
            new
            {
                error = "plan limit reached",
                decision.Code,
                decision.Limit,
                decision.Current,
            },
            statusCode: StatusCodes.Status402PaymentRequired
        );

    /// <summary>Gate 1 for a boolean entitlement the plan does not include.</summary>
    public static IResult FeatureOff(string code) =>
        Results.Json(
            new { error = "plan does not include this feature", code },
            statusCode: StatusCodes.Status402PaymentRequired
        );
}
