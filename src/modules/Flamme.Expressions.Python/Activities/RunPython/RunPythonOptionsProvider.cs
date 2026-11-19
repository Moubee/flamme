using System.Reflection;
using Flamme.Workflows.UIHints.CodeEditor;

// ReSharper disable once CheckNamespace
namespace Flamme.Expressions.Python.Activities;

internal class RunPythonOptionsProvider : CodeEditorOptionsProviderBase
{
    protected override string GetLanguage(PropertyInfo propertyInfo, object? context) => "python";
}