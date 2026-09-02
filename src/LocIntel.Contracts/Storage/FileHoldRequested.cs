namespace LocIntel.Contracts.Storage;

/// <summary>
/// Cross-module WRITE (ADR 17): a case places or lifts legal hold on the
/// files it holds as evidence; Storage applies it over the outbox with the
/// tenant on the envelope. Reason lands in Storage's own domain audit.
/// </summary>
public sealed record FileHoldRequested(Guid FileId, bool Hold, string Reason);
