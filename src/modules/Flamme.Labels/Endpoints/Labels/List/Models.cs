using Flamme.Common.Models;
using Flamme.Labels.Entities;

namespace Flamme.Labels.Endpoints.Labels.List;

public class Request
{
    public int? Page { get; set; }
    public int? PageSize { get; set; }

    public PageArgs ToPageArgs() => PageArgs.FromPage(Page, PageSize);
}

public class Response
{
    public ICollection<Label> Items { get; set; } = default!;
    public long TotalCount { get; set; }
}