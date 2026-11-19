using Flamme.WorkflowProviders.BlobStorage.Contracts;

namespace Flamme.Extensions;

public static class BlobWorkflowFormatHandlerExtensions
{
    public static bool SupportsExtension(this IBlobWorkflowFormatHandler handler, string extension) => handler.SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
}