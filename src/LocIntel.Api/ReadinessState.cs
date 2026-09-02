namespace LocIntel.Api;

/// <summary>
/// /healthz gates on this: 503 until ready. In Development the DevBootstrap
/// (migrations + seed) flips it, so Aspire's WaitFor(api) - and therefore the
/// console starting - means "the stack is actually usable", closing the
/// cold-start race where an early Sign in click hit a 404.
/// </summary>
public sealed class ReadinessState(bool ready)
{
    private volatile bool _ready = ready;
    public bool Ready => _ready;

    public void MarkReady() => _ready = true;
}
