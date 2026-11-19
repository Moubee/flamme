using Flamme.Common.Models;
using Flamme.Labels.Entities;
using FastEndpoints;

namespace Flamme.Labels.Endpoints.Labels.List;

public class PageMapper : Mapper<Request, Response, Page<Label>>
{
    public override Response FromEntity(Page<Label> e) => new()
    {
        Items = e.Items,
        TotalCount = e.TotalCount
    };
}