namespace CognitivePlatform.Api.Registry.Capabilities;

/// <summary>
/// Minimal service interface that CRUD capability templates delegate to.
/// Domain services adapt this interface to bridge the generic template with
/// their specific storage layer.
/// </summary>
public interface ICrudService<TEntity> where TEntity : class
{
    Task<TEntity>                AddAsync(IDictionary<string, string> parameters, CancellationToken cancellationToken);
    Task<IReadOnlyList<TEntity>> ListAsync(CancellationToken cancellationToken);
    Task<TEntity?>               GetAsync(string id, CancellationToken cancellationToken);
    Task<bool>                   DeleteAsync(string id, CancellationToken cancellationToken);

    /// <summary>Formats a single entity for list output using the standard format.</summary>
    string FormatForList(TEntity entity);

    /// <summary>Formats a single entity for get/detail output.</summary>
    string FormatForDetail(TEntity entity);
}
