using System.ComponentModel;
using Flamme.Workflows;

namespace Flamme.Testing.Shared.Activities;

[InheritedDescription]
public abstract class AnnotatedProducerActivity : CodeActivity;

[AttributeUsage(AttributeTargets.Class, Inherited = true)]
internal sealed class InheritedDescriptionAttribute() : DescriptionAttribute("Inherited producer description");
