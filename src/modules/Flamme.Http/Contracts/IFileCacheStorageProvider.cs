using FluentStorage.Blobs;

namespace Flamme.Http;

/// <summary>
/// Represents a provider of a file cache storage.
/// </summary>
public interface IFileCacheStorageProvider
{
    /// <summary>
    /// Gets the storage.
    /// </summary>
    /// <returns></returns>
    IBlobStorage GetStorage();
}