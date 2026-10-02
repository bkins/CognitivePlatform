namespace CognitivePlatform.Api.Companion;

public sealed class CompanionRequestGate
{
    public SemaphoreSlim Semaphore { get; } = new(2, 2);
}
