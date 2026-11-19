using System.Linq.Expressions;
using Flamme.Common.Entities;
using Flamme.Workflows.Runtime.Entities;

namespace Flamme.Workflows.Runtime.OrderDefinitions;

public class StoredTriggerOrder<TProp> : OrderDefinition<StoredTrigger, TProp>
{
    public StoredTriggerOrder(Expression<Func<StoredTrigger, TProp>> keySelector, OrderDirection direction)
    {
        KeySelector = keySelector;
        Direction = direction;
    }
}