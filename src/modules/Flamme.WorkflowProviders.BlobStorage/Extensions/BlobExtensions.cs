using FluentStorage.Blobs;

namespace Flamme.Extensions;

public static class BlobExtensions
{
    public static string GetExtension(this Blob blob) => Path.GetExtension(blob.Name).TrimStart('.');
}