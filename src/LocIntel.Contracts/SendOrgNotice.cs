namespace LocIntel.Contracts;

/// <summary>
/// "Tell the people who manage this org something operational" (billing
/// state, lifecycle changes). Handled by Identity - the one module that
/// knows who holds org:manage and how to reach them. Tenant rides the
/// envelope; publishers never resolve recipients themselves.
/// </summary>
public sealed record SendOrgNotice(string Subject, string[] BodyLines);
