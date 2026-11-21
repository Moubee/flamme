using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Flamme.ActivityGenerator.Models;

internal sealed record ActivityGenerationResult(string? Source, ImmutableArray<Diagnostic> Diagnostics);
