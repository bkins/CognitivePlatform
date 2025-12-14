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
    string Save<T> (T       value
                  , string? partitionKey = null
                  , string? id           = null);

    /// <summary>
    /// Retrieves an object by Id (and optional partition) or null if not found / soft-deleted.
    /// </summary>
    T? Get<T> (string  id
             , string? partitionKey = null);

    /// <summary>
    /// Lists all objects of type <typeparamref name="T"/> filtered by:
    /// - optional partitionKey
    /// - optional CreatedUtc range
    /// </summary>
    IReadOnlyList<T> List<T> (string?         partitionKey = null
                            , DateTimeOffset? fromUtc      = null
                            , DateTimeOffset? toUtc        = null);

    /// <summary>
    /// Marks an object as deleted without physically removing it from storage.
    /// </summary>
    bool SoftDelete<T> (string  id
                      , string? partitionKey = null);
}