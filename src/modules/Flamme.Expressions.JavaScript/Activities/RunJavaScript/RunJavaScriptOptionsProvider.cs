using System.Reflection;
using Flamme.Workflows.UIHints.CodeEditor;

// ReSharper disable once CheckNamespace
namespace Flamme.Expressions.JavaScript.Activities;

internal class RunJavaScriptOptionsProvider : CodeEditorOptionsProviderBase
{
    protected override string GetLanguage(PropertyInfo propertyInfo, object? context) => "javascript";
}