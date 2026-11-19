using System.Runtime.CompilerServices;
using Flamme.Expressions.Models;
using Flamme.Workflows.Attributes;
using Flamme.Workflows.Models;

namespace Flamme.Workflows.Activities;

/// <summary>
/// Read a line of text from the console.
/// </summary>
[Activity("Console", "ReadLine", 1, "Read a line of text from the console.", "Console")]
public class ReadLine : CodeActivity<string>
{
    /// <inheritdoc />
    public ReadLine([CallerFilePath] string? source = null, [CallerLineNumber] int? line = null) : base(source, line)
    {
    }

    /// <inheritdoc />
    public ReadLine(MemoryBlockReference output, [CallerFilePath] string? source = null, [CallerLineNumber] int? line = null) : base(output, source, line)
    {
    }

    /// <inheritdoc />
    public ReadLine(Output<string>? output, [CallerFilePath] string? source = null, [CallerLineNumber] int? line = null) : base(output, source, line)
    {
    }

    /// <inheritdoc />
    protected override void Execute(ActivityExecutionContext context)
    {
        var provider = context.GetService<IStandardInStreamProvider>() ?? new StandardInStreamProvider(Console.In);
        var reader = provider.GetTextReader();
        var text = reader.ReadLine()!;
        context.Set(Result, text);
    }
}