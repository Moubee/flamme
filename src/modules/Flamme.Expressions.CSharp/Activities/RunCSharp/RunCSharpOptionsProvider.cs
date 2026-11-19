using System.Reflection;
using Flamme.Workflows.UIHints.CodeEditor;

// ReSharper disable once CheckNamespace
namespace Flamme.Expressions.CSharp.Activities;

internal class RunCSharpOptionsProvider : CodeEditorOptionsProviderBase
{
    protected override string GetLanguage(PropertyInfo propertyInfo, object? context) => "csharp";
}