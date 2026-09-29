using System.Net;
using System.Text;

namespace HoneyDrunk.Identity.Tests.Fixtures;

internal sealed class GraphTestHandler(IEnumerable<HttpStatusCode> statuses, string body = "{}") : HttpMessageHandler
{
    private readonly Queue<HttpStatusCode> remaining = new(statuses);

    public List<string> Requests { get; } = [];

    public List<string> Hosts { get; } = [];

    public bool Cancel { get; init; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Cancel)
            throw new TaskCanceledException("Isolated simulated provider timeout.");
        Assert.Equal("isolated-test-credential", request.Headers.Authorization?.Parameter);
        Requests.Add(request.Method + " " + request.RequestUri!.PathAndQuery);
        Hosts.Add(request.RequestUri.Host);
        return Task.FromResult(new HttpResponseMessage(remaining.Dequeue()) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
}
