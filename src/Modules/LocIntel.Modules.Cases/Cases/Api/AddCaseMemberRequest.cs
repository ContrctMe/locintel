namespace LocIntel.Modules.Cases.Cases.Api;

public sealed record AddCaseMemberRequest(
    Guid UserId,
    CaseMemberRole Role = CaseMemberRole.Investigator
);
