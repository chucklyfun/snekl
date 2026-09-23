using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Snekl.Faker;

[ApiController]
public class FakerAdminController : ControllerBase
{
    private static readonly (string Username, string DisplayName)[] Personas =
    [
        ("faker_mira", "Mira"),
        ("faker_kenji", "Kenji"),
        ("faker_riley", "Riley"),
        ("faker_sage", "Sage"),
        ("faker_quinn", "Quinn"),
        ("faker_harper", "Harper"),
        ("faker_nico", "Nico"),
        ("faker_jules", "Jules")
    ];

    private readonly FakerStore _store;
    private readonly FakerClient _client;
    private readonly FakerTokenService _tokens;
    private readonly FakerApiLimits _limits;
    private readonly FakerOptions _options;

    public FakerAdminController(
        FakerStore store,
        FakerClient client,
        FakerTokenService tokens,
        FakerApiLimits limits,
        IOptions<FakerOptions> options)
    {
        _store = store;
        _client = client;
        _tokens = tokens;
        _limits = limits;
        _options = options.Value;
    }

    [HttpGet("/.well-known/snekl-extension.json")]
    public IActionResult Manifest() => Ok(new
    {
        id = _options.ClientId,
        name = "Snekl Faker",
        version = "0.1.0",
        adminUrl = _options.PublicBaseUrl.TrimEnd('/') + "/",
        iframeUrl = (string?)null,
        description = "Creates bot identities and fake forum traffic for local UI and realtime testing."
    });

    [HttpGet("/api/faker/status")]
    public async Task<ActionResult<object>> Status(CancellationToken ct)
    {
        await _tokens.RefreshExpiredAsync(ct);
        var known = _options.ResolvedContentUrls();
        return Ok(await _store.ReadAsync(state => new
        {
            posting = state.Posting,
            intervalSeconds = state.IntervalSeconds,
            lastAction = state.LastAction,
            lastError = state.LastError,
            nodes = known.Select(url => NodeDto(url, state, state.FindNode(url))).ToList()
        }, ct));
    }

    [HttpGet("/api/faker/connect")]
    public async Task<IActionResult> Connect([FromQuery] string? content, CancellationToken ct)
    {
        var url = ResolveContentUrl(content);
        if (url is null)
            return BadRequest(new { error = "Unknown content server." });

        var redirect = await _store.UpdateAsync(state =>
        {
            state.PendingVerifier = FakerPkce.NewVerifier();
            state.PendingState = Guid.NewGuid().ToString("N");
            state.PendingContentUrl = url;
            return _client.BuildAuthorizeUrl(state.PendingVerifier, state.PendingState, url);
        }, ct);
        return Redirect(redirect);
    }

    [HttpGet("/oauth/callback")]
    public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(error))
        {
            await _store.UpdateAsync(s => s.LastError = error, ct);
            return Redirect("/?error=" + Uri.EscapeDataString(error));
        }

        var pending = await _store.ReadAsync(s => (s.PendingVerifier, s.PendingState, s.PendingContentUrl), ct);
        if (string.IsNullOrWhiteSpace(code) || pending.PendingVerifier is null || pending.PendingState != state)
        {
            await _store.UpdateAsync(s => s.LastError = "OAuth callback was missing code or state.", ct);
            return Redirect("/?error=oauth");
        }

        try
        {
            var token = await _client.PostAuthAsync("/api/oauth/token", new
            {
                grantType = "authorization_code",
                code,
                redirectUri = _client.RedirectUri,
                clientId = _options.ClientId,
                clientSecret = _options.ClientSecret,
                codeVerifier = pending.PendingVerifier
            }, null, ct);

            var contentUrl = token.TryGetProperty("contentBaseUrl", out var fromToken)
                ? FakerTargets.Canonical(fromToken.GetString(), _options.ResolvedContentUrls())
                : FakerTargets.Canonical(pending.PendingContentUrl, _options.ResolvedContentUrls());
            if (string.IsNullOrWhiteSpace(contentUrl))
                throw new InvalidOperationException("Token was missing contentBaseUrl.");

            _limits.Remember(contentUrl, token);
            await _store.UpdateAsync(stored =>
            {
                var node = stored.Node(contentUrl);
                node.InstallAccessToken = token.GetProperty("accessToken").GetString();
                node.InstallRefreshToken = token.GetProperty("refreshToken").GetString();
                node.InstallId = token.GetProperty("installId").GetGuid();
                node.InstallExpiresAt = FakerTargets.ReadTokenExpiry(token, node.InstallAccessToken);
                node.LastError = null;
                node.LastAction = "Connected";
                if (stored.PendingState == state)
                {
                    stored.PendingVerifier = null;
                    stored.PendingState = null;
                    stored.PendingContentUrl = null;
                }
                stored.LastError = null;
                stored.LastAction = $"Connected to {contentUrl}";
            }, ct);
            var bots = await _tokens.RefreshBotsAsync(contentUrl, force: true, ct);
            if (bots > 0)
            {
                await _store.UpdateAsync(s =>
                {
                    var node = s.FindNode(contentUrl);
                    if (node is not null)
                    {
                        node.LastError = null;
                        node.LastAction = $"Connected; refreshed {bots} bot token(s)";
                    }
                    s.LastError = null;
                    s.LastAction = $"Connected to {contentUrl}";
                }, ct);
            }
            return Redirect("/");
        }
        catch (Exception ex)
        {
            await _store.UpdateAsync(s => s.LastError = ex.Message, ct);
            return Redirect("/?error=" + Uri.EscapeDataString(ex.Message));
        }
    }

    public record NodeActionRequest(string? Content = null, int Count = 8);

    [HttpPost("/api/faker/bots")]
    public async Task<IActionResult> CreateBots([FromBody] NodeActionRequest? request, CancellationToken ct)
    {
        var url = ResolveContentUrl(request?.Content);
        var token = url is null
            ? null
            : await _store.ReadAsync(s => s.FindNode(url)?.InstallAccessToken, ct);
        if (url is null || string.IsNullOrWhiteSpace(token))
            return BadRequest(new { error = "Connect this app to that node first." });

        var count = Math.Clamp(request?.Count ?? 8, 1, Personas.Length);
        try
        {
            var bots = new List<FakerBotState>();
            for (var i = 0; i < count; i++)
            {
                var persona = Personas[i];
                var suffix = Guid.NewGuid().ToString("N")[..4];
                var created = await _client.PostAuthAsync("/api/oauth/bots", new
                {
                    username = $"{persona.Username}_{suffix}",
                    displayName = persona.DisplayName
                }, token, ct);

                bots.Add(new FakerBotState
                {
                    Username = created.GetProperty("bot").GetProperty("username").GetString() ?? persona.Username,
                    DisplayName = persona.DisplayName,
                    AccessToken = created.GetProperty("accessToken").GetString() ?? "",
                    RefreshToken = created.GetProperty("refreshToken").GetString() ?? "",
                    AccessExpiresAt = created.GetProperty("expiresAt").GetDateTime()
                });
            }

            await _store.UpdateAsync(state =>
            {
                var node = state.FindNode(url);
                if (node is null)
                    return;
                node.Bots = bots;
                node.LastAction = $"Created {node.Bots.Count} bots";
                node.LastError = null;
                state.LastAction = $"{node.LastAction} on {node.ContentBaseUrl}";
                state.LastError = null;
            }, ct);
            return Ok(new { content = url, bots = bots.Select(b => b.DisplayName) });
        }
        catch (Exception ex)
        {
            await _store.UpdateAsync(state =>
            {
                var node = state.FindNode(url);
                if (node is not null)
                    node.LastError = ex.Message;
                state.LastError = ex.Message;
            }, ct);
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("/api/faker/fill")]
    public async Task<IActionResult> Fill([FromBody] NodeActionRequest? request, CancellationToken ct)
    {
        var url = ResolveContentUrl(request?.Content);
        var snapshot = url is null
            ? null
            : await _store.ReadAsync(s => s.FindNode(url), ct);
        if (url is null || snapshot is null || snapshot.Bots.Count == 0)
            return BadRequest(new { error = "Create bots on that node first." });

        try
        {
            var lead = snapshot.Bots[0];
            var forumIds = new List<Guid>();
            foreach (var (name, slugPrefix, description) in new[]
            {
                ("Sandbox", "sandbox-", "Faker playground. Local-only for now."),
                ("Off-topic", "offtopic-", "Side chatter"),
            })
            {
                var created = await _client.PostContentAsync(url, "/api/forums", new
                {
                    name,
                    slug = slugPrefix + Guid.NewGuid().ToString("N")[..6],
                    description
                }, lead.AccessToken, ct);
                forumIds.Add(created.GetProperty("id").GetGuid());
            }

            var threadIds = new List<Guid>();
            var postIds = new List<Guid>();
            var upvotedKeys = new List<string>();
            var titles = new[]
            {
                ("Welcome to the sandbox", "This node was empty, so Faker filled it. Watch the thread for live replies."),
                ("Realtime check", "Keep this open while keep-posting is on."),
                ("Nested replies", "Reply depth is the point of a forum, not a flat feed.")
            };
            for (var i = 0; i < titles.Length; i++)
            {
                var bot = snapshot.Bots[i % snapshot.Bots.Count];
                var forumId = forumIds[i % forumIds.Count];
                var thread = await _client.PostContentAsync(url, "/api/posts", new
                {
                    forumId,
                    parentPostId = (Guid?)null,
                    title = titles[i].Item1,
                    message = titles[i].Item2
                }, bot.AccessToken, ct);
                var threadId = FakerTargets.ReadId(thread);
                threadIds.Add(threadId);
                postIds.Add(threadId);

                var replier = snapshot.Bots[(i + 1) % snapshot.Bots.Count];
                var reply = await _client.PostContentAsync(url, "/api/posts", new
                {
                    forumId,
                    parentPostId = threadId,
                    message = $"Noted, {bot.DisplayName}."
                }, replier.AccessToken, ct);
                postIds.Add(FakerTargets.ReadId(reply));
            }

            var rng = Random.Shared;
            foreach (var postId in postIds)
            {
                var take = threadIds.Contains(postId) ? snapshot.Bots.Count : Math.Min(4, snapshot.Bots.Count);
                var voters = snapshot.Bots.OrderBy(_ => rng.Next()).Take(take).ToList();
                foreach (var voter in voters)
                {
                    JsonElement vote;
                    try
                    {
                        vote = await _client.PostContentAsync(url, $"/api/posts/{postId:D}/upvote", null, voter.AccessToken, ct);
                    }
                    catch (InvalidOperationException ex) when (ex.Message.Contains("cannot upvote your own post", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    var upvoted = vote.ValueKind == JsonValueKind.Object
                                  && vote.TryGetProperty("viewerHasUpvoted", out var flag)
                                  && flag.GetBoolean();
                    if (upvoted)
                        upvotedKeys.Add(FakerTargets.VoteKey(voter.Username, postId));
                }
            }

            await _store.UpdateAsync(state =>
            {
                var node = state.FindNode(url);
                if (node is null)
                    return;
                node.ForumIds = forumIds;
                node.ThreadIds = threadIds;
                node.PostIds = postIds;
                node.UpvotedKeys = upvotedKeys.Distinct(StringComparer.Ordinal).ToList();
                node.LastAction = "Filled forums, seed threads, replies, and upvotes";
                node.LastError = null;
                state.LastAction = $"Filled {node.ContentBaseUrl}";
                state.LastError = null;
            }, ct);
            return Ok(new { content = url, forums = forumIds.Count, threads = threadIds.Count, upvotes = upvotedKeys.Count });
        }
        catch (Exception ex)
        {
            await _store.UpdateAsync(state =>
            {
                var node = state.FindNode(url);
                if (node is not null)
                    node.LastError = ex.Message;
                state.LastError = ex.Message;
            }, ct);
            return BadRequest(new { error = ex.Message });
        }
    }

    public record PostingRequest(bool Enabled, int IntervalSeconds = 5);

    [HttpPost("/api/faker/posting")]
    public async Task<IActionResult> Posting([FromBody] PostingRequest request, CancellationToken ct)
    {
        var result = await _store.UpdateAsync(state =>
        {
            if (request.Enabled && !state.Nodes.Any(n => n.ReadyToPost))
                return (false, state.Posting, state.IntervalSeconds, "Connect, create bots, and fill at least one node first.");
            state.Posting = request.Enabled;
            state.IntervalSeconds = Math.Clamp(request.IntervalSeconds, 2, 60);
            state.LastAction = request.Enabled ? "Keep posting on" : "Keep posting off";
            return (true, state.Posting, state.IntervalSeconds, (string?)null);
        }, ct);
        if (!result.Item1)
            return BadRequest(new { error = result.Item4 });
        return Ok(new { posting = result.Item2, intervalSeconds = result.Item3 });
    }

    [HttpPost("/api/faker/disconnect")]
    public async Task<IActionResult> Disconnect([FromBody] NodeActionRequest? request, CancellationToken ct)
    {
        var url = ResolveContentUrl(request?.Content);
        if (url is null)
            return BadRequest(new { error = "Unknown content server." });

        await _store.UpdateAsync(state =>
        {
            var node = state.FindNode(url);
            if (node is not null)
            {
                node.InstallAccessToken = null;
                node.InstallRefreshToken = null;
                node.InstallId = null;
                node.InstallExpiresAt = null;
                node.LastError = null;
                node.LastAction = "Disconnected";
            }

            if (FakerTargets.SameTarget(state.PendingContentUrl, url))
            {
                state.PendingVerifier = null;
                state.PendingState = null;
                state.PendingContentUrl = null;
            }

            state.LastError = null;
            state.LastAction = $"Disconnected {url}";
        }, ct);
        return Ok(new { content = url, connection = "disconnected" });
    }

    [HttpPost("/api/faker/clear")]
    public async Task<IActionResult> Clear([FromBody] NodeActionRequest? request, CancellationToken ct)
    {
        var url = ResolveContentUrl(request?.Content);
        if (url is null)
            return BadRequest(new { error = "Unknown content server." });

        await _store.UpdateAsync(state =>
        {
            var node = state.FindNode(url);
            if (node is not null)
            {
                node.Bots.Clear();
                node.ForumIds.Clear();
                node.ThreadIds.Clear();
                node.PostIds.Clear();
                node.UpvotedKeys.Clear();
                node.LastError = null;
                node.LastAction = "Cleared test data";
            }

            state.LastError = null;
            state.LastAction = $"Cleared test data on {url}";
        }, ct);
        return Ok(new { content = url });
    }

    private string? ResolveContentUrl(string? content)
    {
        var urls = _options.ResolvedContentUrls();
        if (string.IsNullOrWhiteSpace(content))
            return urls[0];
        return urls.FirstOrDefault(u => FakerTargets.SameTarget(u, content));
    }

    private static object NodeDto(string url, FakerState state, FakerNodeState? node)
    {
        var connection = FakerTargets.ConnectionStatus(state, url, node);
        var expires = node?.InstallExpiresAt ?? FakerTargets.ReadJwtExpiry(node?.InstallAccessToken);
        return new
        {
            contentBaseUrl = url,
            connected = connection == "connected",
            connection,
            installExpiresAt = expires,
            installId = node?.InstallId,
            botCount = node?.Bots.Count ?? 0,
            bots = node?.Bots.Select(b => b.DisplayName).ToList() ?? [],
            forumCount = node?.ForumIds.Count ?? 0,
            threadCount = node?.ThreadIds.Count ?? 0,
            lastAction = node?.LastAction,
            lastError = node?.LastError
        };
    }
}
