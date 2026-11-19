using Flamme.Expressions.Models;
using Flamme.Extensions;
using Flamme.Workflows.Models;

namespace Flamme.Workflows.IntegrationTests.Scenarios.CanExecute.Activities;

public class CustomActivity : CodeActivity
{
    public CustomActivity(int magicNumber)
    {
        MagicNumber = new (magicNumber);
    }
    
    public CustomActivity(Func<ExpressionExecutionContext, int> magicNumber)
    {
        MagicNumber = new (magicNumber);
    }
    
    public Input<int> MagicNumber { get; set; }
    
    protected override bool CanExecute(ActivityExecutionContext context)
    {
        var magicNumber = MagicNumber.Get(context);
        return magicNumber == 42;
    }

    protected override void Execute(ActivityExecutionContext context)
    {
        var provider = context.GetService<IStandardOutStreamProvider>() ?? new StandardOutStreamProvider(Console.Out);
        var textWriter = provider.GetTextWriter();
        textWriter.WriteLine("Welcome to the world of Might and Magic!");
    }
}