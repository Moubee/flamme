using Flamme.Alterations.Core.Abstractions;
using JetBrains.Annotations;

namespace Flamme.Alterations.AlterationTypes;

/// <summary>
/// Cancels the workflow instances in an alteration plan.
/// </summary>
[UsedImplicitly]
public class Cancel : AlterationBase;