using Flamme.Workflows.Activities;

namespace Flamme.Activities.IntegrationTests.Composition.Activities;

/// <summary>
/// Outer composite activity for nested composite testing.
/// </summary>
public class OuterComposite : Composite
{
    public OuterComposite()
    {
        Root = new Sequence
        {
            Activities =
            {
                new InnerComposite(),
                new WriteLine("After inner composite")
            }
        };
    }
}