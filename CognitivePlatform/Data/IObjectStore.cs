using System;
using System.Collections.Generic;

namespace CognitivePlatform.Api.Data;

public interface IObjectStore
{
    /// <summary>
    /// Saves an object of type <typeparamref name="T"/> into the store.
    /// If <paramref name="id"/> is null/empty, the store will:
    /// - Try to use a public string property named "Id" on the object
    /// - Otherwise generate a new GUID string
    /// The resolved Id is returned and (if possible) written back to the object's "Id" property.
    /// </summary>
    Task<string> Save<T> (T       value
                        , string? partitionKey = null
                        , string? id           = null);

    /// <summary>
    /// Retrieves an object by Id (and optional partition) or null if not found / soft-deleted.
    /// </summary>
    T? Get<T> (string  id
             , string? partitionKey = null);
    T? GetDeleted<T> (string  id
                    , string? partitionKey = null);

    /// <summary>Async variant of <see cref="Get{T}"/>.</summary>
    Task<T?> GetAsync<T>( string            id
                        , string?           partitionKey      = null
                        , CancellationToken cancellationToken = default );

    /// <summary>
    /// Lists all objects of type <typeparamref name="T"/> filtered by:
    /// - optional partitionKey
    /// - optional CreatedUtc range
    /// </summary>
    IReadOnlyList<T> List<T> (string?         partitionKey = null
                            , DateTimeOffset? fromUtc      = null
                            , DateTimeOffset? toUtc        = null);

    /// <summary>Async variant of <see cref="List{T}"/>.</summary>
    Task<IReadOnlyList<T>> ListAsync<T>( string?           partitionKey      = null
                                       , DateTimeOffset?   fromUtc           = null
                                       , DateTimeOffset?   toUtc             = null
                                       , CancellationToken cancellationToken = default );

    /// <summary>
    /// Marks an object as deleted without physically removing it from storage.
    /// </summary>
    bool SoftDelete<T> (string  id
                      , string? partitionKey = null);

    /// <summary>Async variant of <see cref="SoftDelete{T}"/>.</summary>
    Task<bool> SoftDeleteAsync<T>( string            id
                                 , string?           partitionKey      = null
                                 , CancellationToken cancellationToken = default );
}