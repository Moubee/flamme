using System.Reflection;
using Flamme.Workflows.UIHints.CodeEditor;

// ReSharper disable once CheckNamespace
namespace Flamme.Workflows.UIHints.JsonEditor;

internal class JsonCodeOptionsProvider : CodeEditorOptionsProviderBase
{
    protected override string GetLanguage(PropertyInfo propertyInfo, object? context) => "json";
}