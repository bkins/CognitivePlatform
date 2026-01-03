namespace CognitivePlatform.Api.Avails;

public static class StartupState
{
    private static int _ready;

    public static bool IsReady => _ready == 1;

    public static void MarkReady()
    {
        Interlocked.Exchange(ref _ready, 1);
    }
}

