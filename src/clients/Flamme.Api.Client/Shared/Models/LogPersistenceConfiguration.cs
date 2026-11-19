using Flamme.Api.Client.Resources.Scripting.Models;
using Flamme.Api.Client.Shared.Enums;

namespace Flamme.Api.Client.Shared.Models;

public class LogPersistenceConfiguration
{
    public LogPersistenceEvaluationMode EvaluationMode { get; set; }
    public string? StrategyType { get; set; }
    public Expression? Expression { get; set; }
}