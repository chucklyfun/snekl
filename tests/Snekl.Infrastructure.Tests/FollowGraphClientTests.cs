using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Snekl.Infrastructure.Services;

namespace Snekl.Infrastructure.Tests;

public class FollowGraphClientTests
{
    [Fact]
    public async Task FollowsAsync_MissingIssuerOrDids_IsFalse()
    {
        var client = new FollowGraphClient(new HttpClient(new SequenceHandler()), NullLogger<FollowGraphClient>.Instance);
        Assert.False(await client.FollowsAsync(null, "did:a", "did:b"));
        Assert.False(await client.FollowsAsync("http://auth.test", "", "did:b"));
        Assert.False(await client.FollowsAsync("http://auth.test", "did:a", " "));
    }

    [Fact]
    public async Task FollowsAsync_HttpError_FailsClosed()
    {
        var handler = new SequenceHandler
        {
            Responses = { new HttpResponseMessage(HttpStatusCode.InternalServerError) }
        };
        var client = new FollowGraphClient(new HttpClient(handler) { BaseAddress = new Uri("http://unused/") }, NullLogger<FollowGraphClient>.Instance);
        Assert.False(await client.FollowsAsync("http://auth.test", "did:op", "did:viewer"));
    }

    [Fact]
    public async Task FollowsAsync_CachesTrue_EvenIfAuthWouldLaterSayFalse()
    {
        var handler = new SequenceHandler
        {
            Responses =
            {
                Json(HttpStatusCode.OK, """{"follows":true}"""),
                Json(HttpStatusCode.OK, """{"follows":false}""")
            }
        };
        var client = new FollowGraphClient(new HttpClient(handler), NullLogger<FollowGraphClient>.Instance);
        Assert.True(await client.FollowsAsync("http://auth.test/", "did:op", "did:viewer"));
        Assert.True(await client.FollowsAsync("http://auth.test", "did:op", "did:viewer"));
        Assert.Single(handler.Requests);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private sealed class SequenceHandler : HttpMessageHandler
    {
        public List<HttpResponseMessage> Responses { get; } = [];
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (Responses.Count == 0)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            var next = Responses[0];
            Responses.RemoveAt(0);
            return Task.FromResult(next);
        }
    }
}
