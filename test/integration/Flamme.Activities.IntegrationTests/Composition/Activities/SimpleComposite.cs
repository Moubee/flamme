using Flamme.Workflows.Activities;

namespace Flamme.Activities.IntegrationTests.Composition.Activities;

/// <summary>
/// Simple composite activity that uses Complete to terminate early.
/// </summary>
public class SimpleComposite : Composite
{
    public SimpleComposite()
    {
        Root = new Sequence
        {
            Activities =
            {
                new WriteLine("Before Complete"),
                new Complete(),
                new WriteLine("This should not execute")
            }
        };
    }
}