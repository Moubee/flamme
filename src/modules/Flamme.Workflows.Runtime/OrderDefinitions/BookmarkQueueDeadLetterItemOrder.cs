using System.Linq.Expressions;
using Flamme.Common.Entities;
using Flamme.Workflows.Runtime.Entities;
using JetBrains.Annotations;

namespace Flamme.Workflows.Runtime.OrderDefinitions;

/// <summary>
/// Represents the order by which to order bookmark queue dead-letter results.
/// </summary>
[UsedImplicitly]
public class BookmarkQueueDeadLetterItemOrder<TProp> : OrderDefinition<BookmarkQueueDeadLetterItem, TProp>
{
    public BookmarkQueueDeadLetterItemOrder(Expression<Func<BookmarkQueueDeadLetterItem, TProp>> keySelector, OrderDirection direction)
    {
        KeySelector = keySelector;
        Direction = direction;
    }
}
