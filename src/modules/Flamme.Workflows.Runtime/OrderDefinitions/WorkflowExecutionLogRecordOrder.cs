using System.Linq.Expressions;
using Flamme.Common.Entities;
using Flamme.Workflows.Runtime.Entities;

namespace Flamme.Workflows.Runtime.OrderDefinitions;

/// <summary>
/// Represents the order by which to order the results of a query.
/// </summary>
public class WorkflowExecutionLogRecordOrder<TProp> : OrderDefinition<WorkflowExecutionLogRecord, TProp>
{
    /// <summary>
    /// Creates a new instance of the <see cref="WorkflowExecutionLogRecordOrder{TProp}"/> class.
    /// </summary>
    public WorkflowExecutionLogRecordOrder(Expression<Func<WorkflowExecutionLogRecord, TProp>> keySelector, OrderDirection direction)
    {
        KeySelector = keySelector;
        Direction = direction;
    }
}