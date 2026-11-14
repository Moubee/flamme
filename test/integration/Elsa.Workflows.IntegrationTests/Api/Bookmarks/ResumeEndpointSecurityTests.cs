using System.Net;
using System.Text;
using Elsa.SasTokens.Contracts;
using Elsa.Workflows.Api.Endpoints.Bookmarks.Resume;
using Elsa.Workflows.Runtime;
using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace Elsa.Workflows.IntegrationTests.Api.Bookmarks;

public class ResumeEndpointSecurityTests
{
    private const int MaxRequestBodySize = 1024 * 1024;
    private readonly ITokenService _tokenService = Mock.Of<ITokenService>().Object;
    private readonly IPayloadSerializer _payloadSerializer = Mock.Of<IPayloadSerializer>().Object;
    private readonly IApiSerializer _apiSerializer = Mock.Of<IApiSerializer>().Object;
    private readonly IWorkflowResumer _workflowResumer = Mock.Of<IWorkflowResumer>().Object;
    private readonly IBookmarkQueue _bookmarkQueue = Mock.Of<IBookmarkQueue>().Object;

    public ResumeEndpointSecurityTests()
    {
        Mock.Get(_tokenService).TryDecryptToken<BookmarkTokenPayload>(Arg.Any<string>())
            .Returns(false)
            .SetsOutPayload(new BookmarkTokenPayload("bookmark", "workflow"));
    }

    [Test]
    public async Task Get_WithInvalidToken_DoesNotParseQueryInput()
    {
        var input = Uri.EscapeDataString("{not-json");
        var context = CreateHttpContext(HttpMethods.Get, $"?t=invalid&async=true&in={input}");
        var sut = CreateEndpoint(context);

        await sut.HandleAsync(CancellationToken.None);

        await Assert.That(context.Response.StatusCode).IsEqualTo((int)HttpStatusCode.BadRequest);
        Mock.Get(_payloadSerializer).Deserialize<IDictionary<string, object>>(Arg.Any<string>()).WasCalled(Times.Exactly(0));
        Mock.Get(_apiSerializer).Deserialize<Request>(Arg.Any<string>()).WasCalled(Times.Exactly(0));
        Mock.Get(_workflowResumer).ResumeAsync(Arg.Any<ResumeBookmarkRequest>(), Arg.Any<CancellationToken>()).WasNeverCalled();
        Mock.Get(_bookmarkQueue).EnqueueAsync(Arg.Any<NewBookmarkQueueItem>(), Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    [Test]
    [Arguments("GET", "?async=true&in=%7Bnot-json", null)]
    [Arguments("POST", "?async=true", """{"input":{"value":"ignored"}}""")]
    public async Task Request_WithMissingToken_DoesNotParseInput(string method, string queryString, string? body)
    {
        var context = CreateHttpContext(method, queryString, body);
        var sut = CreateEndpoint(context);

        await sut.HandleAsync(CancellationToken.None);

        await Assert.That(context.Response.StatusCode).IsEqualTo((int)HttpStatusCode.BadRequest);
        Mock.Get(_apiSerializer).Deserialize<Request>(Arg.Any<string>()).WasCalled(Times.Exactly(0));
        Mock.Get(_payloadSerializer).Deserialize<IDictionary<string, object>>(Arg.Any<string>()).WasCalled(Times.Exactly(0));
        Mock.Get(_workflowResumer).ResumeAsync(Arg.Any<ResumeBookmarkRequest>(), Arg.Any<CancellationToken>()).WasNeverCalled();
        Mock.Get(_bookmarkQueue).EnqueueAsync(Arg.Any<NewBookmarkQueueItem>(), Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    [Test]
    public async Task Post_WithInvalidToken_DoesNotParseBodyInput()
    {
        var context = CreateHttpContext(HttpMethods.Post, "?t=invalid", """{"input":{"value":"ignored"}}""");
        var sut = CreateEndpoint(context);

        await sut.HandleAsync(CancellationToken.None);

        await Assert.That(context.Response.StatusCode).IsEqualTo((int)HttpStatusCode.BadRequest);
        Mock.Get(_apiSerializer).Deserialize<Request>(Arg.Any<string>()).WasCalled(Times.Exactly(0));
        Mock.Get(_payloadSerializer).Deserialize<IDictionary<string, object>>(Arg.Any<string>()).WasCalled(Times.Exactly(0));
        Mock.Get(_workflowResumer).ResumeAsync(Arg.Any<ResumeBookmarkRequest>(), Arg.Any<CancellationToken>()).WasNeverCalled();
        Mock.Get(_bookmarkQueue).EnqueueAsync(Arg.Any<NewBookmarkQueueItem>(), Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    [Test]
    public async Task Post_WithJsonNullBody_ReturnsBadRequest()
    {
        var context = CreateHttpContext(HttpMethods.Post, "?t=valid", "null");
        var sut = CreateEndpoint(context);
        ArrangeValidToken();
        Mock.Get(_apiSerializer).Deserialize<Request>("null").Returns((Request?)null);

        await sut.HandleAsync(CancellationToken.None);

        await Assert.That(context.Response.StatusCode).IsEqualTo((int)HttpStatusCode.BadRequest);
        Mock.Get(_workflowResumer).ResumeAsync(Arg.Any<ResumeBookmarkRequest>(), Arg.Any<CancellationToken>()).WasNeverCalled();
        Mock.Get(_bookmarkQueue).EnqueueAsync(Arg.Any<NewBookmarkQueueItem>(), Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    [Test]
    public async Task Get_WithArgumentExceptionWhileParsingInput_ReturnsBadRequest()
    {
        var context = CreateHttpContext(HttpMethods.Get, "?t=valid&in=%7B%7D");
        var sut = CreateEndpoint(context);
        ArrangeValidToken();
        Mock.Get(_payloadSerializer).Deserialize<IDictionary<string, object>>("{}").Returns(() => throw new ArgumentException("Invalid input."));

        await sut.HandleAsync(CancellationToken.None);

        await Assert.That(context.Response.StatusCode).IsEqualTo((int)HttpStatusCode.BadRequest);
        Mock.Get(_workflowResumer).ResumeAsync(Arg.Any<ResumeBookmarkRequest>(), Arg.Any<CancellationToken>()).WasNeverCalled();
        Mock.Get(_bookmarkQueue).EnqueueAsync(Arg.Any<NewBookmarkQueueItem>(), Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    [Test]
    public async Task Post_WithArgumentExceptionWhileParsingInput_ReturnsBadRequest()
    {
        var context = CreateHttpContext(HttpMethods.Post, "?t=valid", "{}");
        var sut = CreateEndpoint(context);
        ArrangeValidToken();
        Mock.Get(_apiSerializer).Deserialize<Request>("{}").Returns(() => throw new ArgumentException("Invalid input."));

        await sut.HandleAsync(CancellationToken.None);

        await Assert.That(context.Response.StatusCode).IsEqualTo((int)HttpStatusCode.BadRequest);
        Mock.Get(_workflowResumer).ResumeAsync(Arg.Any<ResumeBookmarkRequest>(), Arg.Any<CancellationToken>()).WasNeverCalled();
        Mock.Get(_bookmarkQueue).EnqueueAsync(Arg.Any<NewBookmarkQueueItem>(), Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Post_WithTooLargeBody_ReturnsBadRequest(bool includeContentLength)
    {
        var body = new string('a', MaxRequestBodySize + 1);
        var context = CreateHttpContext(HttpMethods.Post, "?t=valid", body, includeContentLength);
        var sut = CreateEndpoint(context);
        ArrangeValidToken();

        await sut.HandleAsync(CancellationToken.None);

        await Assert.That(context.Response.StatusCode).IsEqualTo((int)HttpStatusCode.BadRequest);
        Mock.Get(_apiSerializer).Deserialize<Request>(Arg.Any<string>()).WasCalled(Times.Exactly(0));
        Mock.Get(_workflowResumer).ResumeAsync(Arg.Any<ResumeBookmarkRequest>(), Arg.Any<CancellationToken>()).WasNeverCalled();
        Mock.Get(_bookmarkQueue).EnqueueAsync(Arg.Any<NewBookmarkQueueItem>(), Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    private Resume CreateEndpoint(DefaultHttpContext context)
    {
        return Factory.Create<Resume>(context, _tokenService, _workflowResumer, _bookmarkQueue, _payloadSerializer, _apiSerializer);
    }

    private void ArrangeValidToken()
    {
        Mock.Get(_tokenService).TryDecryptToken<BookmarkTokenPayload>("valid")
            .Returns(true)
            .SetsOutPayload(new BookmarkTokenPayload("bookmark", "workflow"));
    }

    private static DefaultHttpContext CreateHttpContext(string method, string queryString, string? body = null, bool includeContentLength = true)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.QueryString = new QueryString(queryString);
        context.Response.Body = new MemoryStream();

        if (body == null)
            return context;

        var bodyBytes = Encoding.UTF8.GetBytes(body);
        context.Request.Body = new MemoryStream(bodyBytes);
        if (includeContentLength)
            context.Request.ContentLength = bodyBytes.Length;

        context.Request.ContentType = "application/json";
        return context;
    }
}
