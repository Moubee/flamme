namespace Elsa.Features.Models;

/// <summary>
/// Selects conditional admission independently of feature metadata discovery.
/// </summary>
public enum CompositionPolicy
{
    /// <summary>
    /// Preserves single-pass conditional filtering and the existing late-addition lifecycle.
    /// </summary>
    LegacyV1,

    /// <summary>
    /// Validates every admission batch with fixed-point conditional filtering.
    /// </summary>
    ValidatedV1
}