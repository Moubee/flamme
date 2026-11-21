using Flamme.Activities.UnitTests;
using Flamme.Workflows;

[assembly: ActivityBinding(typeof(StubActivity))]

namespace Flamme.Activities.UnitTests;

internal sealed class StubActivity : CodeActivity;
