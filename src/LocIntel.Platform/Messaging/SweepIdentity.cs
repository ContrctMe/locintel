namespace LocIntel.Platform.Messaging;

/// <summary>A stable lease key for a scheduled message contract.</summary>
public static class SweepIdentity
{
    public static string For<TMessage>() =>
        $"{typeof(TMessage).Assembly.GetName().Name}:{typeof(TMessage).FullName}";
}
