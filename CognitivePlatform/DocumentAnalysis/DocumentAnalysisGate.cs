namespace CognitivePlatform.Api.DocumentAnalysis;

/// <summary>One bounded analysis operation per server; independent of domain stores.</summary>
public sealed class DocumentAnalysisGate
{
    public SemaphoreSlim Semaphore { get; } = new(1, 1);
}
