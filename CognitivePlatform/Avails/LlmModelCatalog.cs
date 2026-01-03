using CognitivePlatform.Api.Avails.Models;

namespace CognitivePlatform.Api.Avails;

public sealed class LlmModelCatalog
{
    private readonly List<LlmModelInfo> _models = new();

    public IReadOnlyList<LlmModelInfo> AvailableModels => _models;

    public void Add (LlmModelInfo model) => _models.Add(model);
}