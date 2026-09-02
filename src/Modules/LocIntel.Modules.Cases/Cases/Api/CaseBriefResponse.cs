namespace LocIntel.Modules.Cases.Cases.Api;

/// <summary>A drafted brief: text for the investigator to read, edit, and file as a note if they choose.</summary>
public sealed record CaseBriefResponse(string Provider, string Text);
