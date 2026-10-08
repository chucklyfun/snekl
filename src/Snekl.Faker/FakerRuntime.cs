using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Snekl.Faker;

public class FakerOptions
{
    public const string SectionName = "Faker";
    public string PublicBaseUrl { get; set; } = "http://localhost:5300";
    public string AuthBaseUrl { get; set; } = "http://localhost:5100";
    public string ContentBaseUrl { get; set; } = "http://localhost:5001";
    public string[] ContentBaseUrls { get; set; } = [];
    public string FrontendBaseUrl { get; set; } = "http://localhost:5173";
    public string ClientId { get; set; } = "snekl-faker";
    public string ClientSecret { get; set; } = "snekl-faker-dev-secret-change-me";

    public IReadOnlyList<string> ResolvedContentUrls()
    {
        var urls = new List<string>();
        foreach (var raw in ContentBaseUrls ?? [])
        {
            var url = FakerTargets.Normalize(raw);
            if (url.Length > 0)
                urls.Add(url);
        }

        var fallback = FakerTargets.Normalize(ContentBaseUrl);
        if (fallback.Length > 0 && !urls.Contains(fallback, StringComparer.OrdinalIgnoreCase))
            urls.Insert(0, fallback);

        if (urls.Count == 0)
            urls.Add("http://localhost:5001");
        return urls.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}

public static class FakerTargets
{
    public static string Normalize(string? url) => (url ?? "").Trim().TrimEnd('/');

    public static bool SameTarget(string? left, string? right)
    {
        var a = Normalize(left);
        var b = Normalize(right);
        if (a.Length == 0 || b.Length == 0)
            return false;
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!Uri.TryCreate(a, UriKind.Absolute, out var ua) || !Uri.TryCreate(b, UriKind.Absolute, out var ub))
            return false;
        return string.Equals(ua.Scheme, ub.Scheme, StringComparison.OrdinalIgnoreCase)
               && string.Equals(ua.Host, ub.Host, StringComparison.OrdinalIgnoreCase)
               && ua.Port == ub.Port;
    }

    public static string Canonical(string? url, IReadOnlyList<string> known)
    {
        var normalized = Normalize(url);
        return known.FirstOrDefault(k => SameTarget(k, normalized)) ?? normalized;
    }

    public static string VoteKey(string username, Guid postId) => $"{username}:{postId:D}";

    public static Guid ReadId(JsonElement created)
    {
        if (!created.TryGetProperty("id", out var id))
            throw new InvalidOperationException("Response was missing id.");
        if (id.ValueKind == JsonValueKind.String && Guid.TryParse(id.GetString(), out var parsed))
            return parsed;
        if (id.TryGetGuid(out parsed))
            return parsed;
        throw new InvalidOperationException("Response id was not a guid.");
    }

    public static DateTime? ReadJwtExpiry(string? jwt)
    {
        if (string.IsNullOrWhiteSpace(jwt))
            return null;
        var parts = jwt.Split('.');
        if (parts.Length < 2)
            return null;
        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload += (payload.Length % 4) switch
            {
                2 => "==",
                3 => "=",
                _ => ""
            };
            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            if (doc.RootElement.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out var seconds))
                return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
        }
        catch (Exception)
        {
            return null;
        }

        return null;
    }

    public static DateTime? ReadTokenExpiry(JsonElement token, string? accessToken)
    {
        if (token.TryGetProperty("expiresIn", out var expiresIn) && expiresIn.TryGetInt32(out var seconds) && seconds > 0)
            return DateTime.UtcNow.AddSeconds(seconds);
        if (token.TryGetProperty("expiresAt", out var expiresAt))
        {
            if (expiresAt.ValueKind == JsonValueKind.String && DateTime.TryParse(expiresAt.GetString(), out var parsed))
                return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            if (expiresAt.TryGetDateTime(out parsed))
                return parsed.ToUniversalTime();
        }

        return ReadJwtExpiry(accessToken);
    }

    public static bool IsExpiring(DateTime? expiresAt, string? jwt, TimeSpan? skew = null)
    {
        if (string.IsNullOrWhiteSpace(jwt) && expiresAt is null)
            return true;
        var when = expiresAt ?? ReadJwtExpiry(jwt);
        if (when is null)
            return false;
        var utc = when.Value.Kind == DateTimeKind.Local ? when.Value.ToUniversalTime() : DateTime.SpecifyKind(when.Value, DateTimeKind.Utc);
        return utc <= DateTime.UtcNow.Add(skew ?? TimeSpan.FromMinutes(2));
    }

    public static bool IsUnauthorized(Exception ex) =>
        ex.Message.Contains("(401)", StringComparison.Ordinal)
        || ex.Message.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase);

    public static void ApplyBotToken(FakerBotState bot, JsonElement token)
    {
        bot.AccessToken = token.GetProperty("accessToken").GetString() ?? bot.AccessToken;
        if (token.TryGetProperty("refreshToken", out var refresh) && refresh.ValueKind == JsonValueKind.String)
            bot.RefreshToken = refresh.GetString() ?? bot.RefreshToken;
        bot.AccessExpiresAt = ReadTokenExpiry(token, bot.AccessToken) ?? DateTime.UtcNow.AddHours(12);
    }

    public static void ApplyInstallToken(FakerNodeState node, JsonElement token)
    {
        node.InstallAccessToken = token.GetProperty("accessToken").GetString();
        if (token.TryGetProperty("refreshToken", out var refresh) && refresh.ValueKind == JsonValueKind.String)
            node.InstallRefreshToken = refresh.GetString();
        node.InstallExpiresAt = ReadTokenExpiry(token, node.InstallAccessToken);
        if (token.TryGetProperty("installId", out var id) && id.TryGetGuid(out var installId))
            node.InstallId = installId;
    }

    public static string ConnectionStatus(FakerState state, string url, FakerNodeState? node)
    {
        if (!string.IsNullOrWhiteSpace(state.PendingVerifier) && SameTarget(state.PendingContentUrl, url))
            return "connecting";
        if (node is null || string.IsNullOrWhiteSpace(node.InstallAccessToken))
            return string.IsNullOrWhiteSpace(node?.LastError) ? "disconnected" : "error";
        var expires = node.InstallExpiresAt ?? ReadJwtExpiry(node.InstallAccessToken);
        if (expires is { } when && when <= DateTime.UtcNow)
            return "expired";
        return "connected";
    }
}

public sealed class FakerBotState
{
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public DateTime AccessExpiresAt { get; set; }
}

public sealed class FakerNodeState
{
    public string ContentBaseUrl { get; set; } = "";
    public string? InstallAccessToken { get; set; }
    public string? InstallRefreshToken { get; set; }
    public Guid? InstallId { get; set; }
    public DateTime? InstallExpiresAt { get; set; }
    public List<FakerBotState> Bots { get; set; } = [];
    public List<Guid> ForumIds { get; set; } = [];
    public List<Guid> ThreadIds { get; set; } = [];
    public List<Guid> PostIds { get; set; } = [];
    public List<string> UpvotedKeys { get; set; } = [];
    public string? LastError { get; set; }
    public string? LastAction { get; set; }

    public bool Connected => !string.IsNullOrWhiteSpace(InstallAccessToken);
    public bool ReadyToPost => Bots.Count > 0 && ForumIds.Count > 0;
}

public sealed class FakerState
{
    public string? InstallAccessToken { get; set; }
    public string? InstallRefreshToken { get; set; }
    public Guid? InstallId { get; set; }
    public string? ContentBaseUrl { get; set; }
    public string? PendingVerifier { get; set; }
    public string? PendingState { get; set; }
    public string? PendingContentUrl { get; set; }
    public bool Posting { get; set; }
    public int IntervalSeconds { get; set; } = 5;
    public List<FakerBotState> Bots { get; set; } = [];
    public List<Guid> ForumIds { get; set; } = [];
    public List<Guid> ThreadIds { get; set; } = [];
    public string? LastError { get; set; }
    public string? LastAction { get; set; }
    public List<FakerNodeState> Nodes { get; set; } = [];

    public void MigrateLegacy()
    {
        if (Nodes.Count > 0 || string.IsNullOrWhiteSpace(InstallAccessToken))
            return;

        Nodes.Add(new FakerNodeState
        {
            ContentBaseUrl = FakerTargets.Normalize(ContentBaseUrl),
            InstallAccessToken = InstallAccessToken,
            InstallRefreshToken = InstallRefreshToken,
            InstallId = InstallId,
            Bots = Bots,
            ForumIds = ForumIds,
            ThreadIds = ThreadIds,
            LastError = LastError,
            LastAction = LastAction
        });
        InstallAccessToken = null;
        InstallRefreshToken = null;
        InstallId = null;
        Bots = [];
        ForumIds = [];
        ThreadIds = [];
    }

    public FakerNodeState? FindNode(string? contentBaseUrl) =>
        Nodes.FirstOrDefault(n => FakerTargets.SameTarget(n.ContentBaseUrl, contentBaseUrl));

    public FakerNodeState Node(string contentBaseUrl)
    {
        var existing = FindNode(contentBaseUrl);
        if (existing is not null)
            return existing;

        var node = new FakerNodeState { ContentBaseUrl = FakerTargets.Normalize(contentBaseUrl) };
        Nodes.Add(node);
        return node;
    }
}

public class FakerStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private FakerState? _live;

    public FakerStore(IWebHostEnvironment env)
    {
        var dir = Path.Combine(env.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "faker-state.json");
    }

    public async Task<T> ReadAsync<T>(Func<FakerState, T> selector, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return selector(await EnsureLoadedAsync(ct));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateAsync(Action<FakerState> mutate, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var state = await EnsureLoadedAsync(ct);
            mutate(state);
            await PersistAsync(state, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<T> UpdateAsync<T>(Func<FakerState, T> mutate, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var state = await EnsureLoadedAsync(ct);
            var result = mutate(state);
            await PersistAsync(state, ct);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<FakerState> EnsureLoadedAsync(CancellationToken ct)
    {
        if (_live is not null)
            return _live;

        if (!File.Exists(_path))
        {
            _live = new FakerState();
            return _live;
        }

        var json = await File.ReadAllTextAsync(_path, ct);
        _live = JsonSerializer.Deserialize<FakerState>(json, Json) ?? new FakerState();
        _live.MigrateLegacy();
        return _live;
    }

    private async Task PersistAsync(FakerState state, CancellationToken ct)
    {
        await File.WriteAllTextAsync(_path, JsonSerializer.Serialize(state, Json), ct);
    }
}

public static class FakerPkce
{
    public static string NewVerifier()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64Url(bytes);
    }

    public static string S256Challenge(string verifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Base64Url(hash);
    }

    public static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public class FakerClient
{
    private readonly HttpClient _http;
    private readonly FakerOptions _options;
    private readonly FakerApiLimits _limits;

    public FakerClient(IHttpClientFactory http, IOptions<FakerOptions> options, FakerApiLimits limits)
    {
        _http = http.CreateClient("faker");
        _options = options.Value;
        _limits = limits;
    }

    public string RedirectUri => $"{_options.PublicBaseUrl.TrimEnd('/')}/oauth/callback";

    public string BuildAuthorizeUrl(string verifier, string state, string contentBaseUrl)
    {
        var challenge = FakerPkce.S256Challenge(verifier);
        var query =
            $"client_id={Uri.EscapeDataString(_options.ClientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}" +
            $"&state={Uri.EscapeDataString(state)}" +
            $"&code_challenge={Uri.EscapeDataString(challenge)}" +
            "&code_challenge_method=S256" +
            $"&content={Uri.EscapeDataString(FakerTargets.Normalize(contentBaseUrl))}" +
            $"&auth={Uri.EscapeDataString(_options.AuthBaseUrl.TrimEnd('/'))}";
        return $"{_options.FrontendBaseUrl.TrimEnd('/')}/extensions/authorize?{query}";
    }

    public Task<JsonElement> PostAuthAsync(string path, object body, string? bearer, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, $"{_options.AuthBaseUrl.TrimEnd('/')}{path}", body, bearer, $"Auth {path}", ct);

    public Task<JsonElement> RefreshOAuthAsync(string refreshToken, CancellationToken ct) =>
        PostAuthAsync("/api/oauth/token", new
        {
            grantType = "refresh_token",
            refreshToken,
            clientId = _options.ClientId,
            clientSecret = _options.ClientSecret
        }, null, ct);

    public Task<JsonElement> GetContentAsync(string contentBaseUrl, string path, string? bearer, CancellationToken ct) =>
        SendAsync(
            HttpMethod.Get,
            $"{FakerTargets.Normalize(contentBaseUrl)}{path}",
            null,
            bearer,
            $"Content {path}",
            ct);

    public async Task<JsonElement> PostContentAsync(
        string contentBaseUrl,
        string path,
        object? body,
        string bearer,
        CancellationToken ct)
    {
        var url = FakerTargets.Normalize(contentBaseUrl);
        await _limits.WaitAsync(
            url,
            "POST",
            path,
            loadNode => GetContentAsync(url, "/api/node", null, loadNode),
            ct);
        return await SendAsync(HttpMethod.Post, $"{url}{path}", body, bearer, $"Content {path}", ct, retryAfterLimit: true);
    }

    private async Task<JsonElement> SendAsync(
        HttpMethod method,
        string url,
        object? body,
        string? bearer,
        string label,
        CancellationToken ct,
        bool retryAfterLimit = false)
    {
        string json = "";
        HttpResponseMessage? response = null;
        for (var attempt = 0; attempt < (retryAfterLimit ? 2 : 1); attempt++)
        {
            response?.Dispose();
            using var request = new HttpRequestMessage(method, url);
            if (body is not null)
                request.Content = JsonContent.Create(body);
            if (!string.IsNullOrWhiteSpace(bearer))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            response = await _http.SendAsync(request, ct);
            json = await response.Content.ReadAsStringAsync(ct);
            if ((int)response.StatusCode != 429 || attempt == 1 || !retryAfterLimit)
                break;
            var wait = ReadRetryAfterSeconds(response, json);
            if (wait <= 0)
                break;
            await _limits.DelayRetryAfterAsync(wait, ct);
        }

        using (response)
        {
            if (response is null)
                throw new InvalidOperationException($"{label} failed.");
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(TryError(json, $"{label} failed ({(int)response.StatusCode})"));
            if (string.IsNullOrWhiteSpace(json))
                return default;
            return JsonSerializer.Deserialize<JsonElement>(json);
        }
    }

    private static int ReadRetryAfterSeconds(HttpResponseMessage response, string json)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta)
            return Math.Max(1, (int)Math.Ceiling(delta.TotalSeconds));
        if (response.Headers.TryGetValues("Retry-After", out var values)
            && int.TryParse(values.FirstOrDefault(), out var headerSeconds)
            && headerSeconds > 0)
            return headerSeconds;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("retryAfterSeconds", out var raw) && raw.TryGetInt32(out var bodySeconds))
                return bodySeconds;
        }
        catch (JsonException)
        {
            // ignore
        }

        return 0;
    }

    private static string TryError(string json, string fallback)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var error))
                return error.GetString() ?? fallback;
        }
        catch (JsonException)
        {
            // ignore
        }
        return string.IsNullOrWhiteSpace(json) ? fallback : json;
    }
}

public class FakerTokenService
{
    private readonly FakerStore _store;
    private readonly FakerClient _client;
    private readonly FakerApiLimits _limits;
    private readonly ILogger<FakerTokenService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FakerTokenService(FakerStore store, FakerClient client, FakerApiLimits limits, ILogger<FakerTokenService> logger)
    {
        _store = store;
        _client = client;
        _limits = limits;
        _logger = logger;
    }

    public async Task RefreshExpiredAsync(CancellationToken ct = default)
    {
        var urls = await _store.ReadAsync(s => s.Nodes.Select(n => n.ContentBaseUrl).ToList(), ct);
        foreach (var url in urls)
        {
            await RefreshInstallIfNeededAsync(url, ct);
            await RefreshBotsAsync(url, force: false, ct);
        }
    }

    public async Task RefreshInstallIfNeededAsync(string contentUrl, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var snapshot = await _store.ReadAsync(s =>
            {
                var node = s.FindNode(contentUrl);
                if (node is null || string.IsNullOrWhiteSpace(node.InstallRefreshToken))
                    return null;
                if (!FakerTargets.IsExpiring(node.InstallExpiresAt, node.InstallAccessToken))
                    return null;
                return node.InstallRefreshToken;
            }, ct);
            if (snapshot is null)
                return;

            try
            {
                var token = await _client.RefreshOAuthAsync(snapshot, ct);
                _limits.Remember(contentUrl, token);
                await _store.UpdateAsync(s =>
                {
                    var node = s.FindNode(contentUrl);
                    if (node is null)
                        return;
                    FakerTargets.ApplyInstallToken(node, token);
                    node.LastError = null;
                }, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Faker install token refresh failed for {Content}", contentUrl);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> RefreshBotsAsync(string contentUrl, bool force, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var snapshot = await _store.ReadAsync(s =>
            {
                var node = s.FindNode(contentUrl);
                if (node is null)
                    return ((string?)null, Array.Empty<(string Username, string RefreshToken)>());
                var bots = node.Bots
                    .Where(b => force || FakerTargets.IsExpiring(b.AccessExpiresAt, b.AccessToken))
                    .Select(b => (Username: b.Username, RefreshToken: b.RefreshToken ?? ""))
                    .ToArray();
                return (node.InstallAccessToken, bots);
            }, ct);

            var refreshed = 0;
            foreach (var bot in snapshot.Item2)
            {
                try
                {
                    JsonElement token;
                    if (!string.IsNullOrWhiteSpace(bot.RefreshToken))
                    {
                        try
                        {
                            token = await _client.RefreshOAuthAsync(bot.RefreshToken, ct);
                            _limits.Remember(contentUrl, token);
                        }
                        catch (Exception ex) when (!string.IsNullOrWhiteSpace(snapshot.Item1))
                        {
                            _logger.LogInformation(ex, "Refresh failed for {Bot} on {Content}; reissuing from install", bot.Username, contentUrl);
                            token = await _client.PostAuthAsync(
                                "/api/oauth/bots/reissue",
                                new { username = bot.Username },
                                snapshot.Item1,
                                ct);
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(snapshot.Item1))
                    {
                        token = await _client.PostAuthAsync(
                            "/api/oauth/bots/reissue",
                            new { username = bot.Username },
                            snapshot.Item1,
                            ct);
                    }
                    else
                    {
                        continue;
                    }

                    await _store.UpdateAsync(s =>
                    {
                        var live = s.FindNode(contentUrl)?.Bots
                            .FirstOrDefault(b => string.Equals(b.Username, bot.Username, StringComparison.Ordinal));
                        if (live is null)
                            return;
                        FakerTargets.ApplyBotToken(live, token);
                        var node = s.FindNode(contentUrl);
                        if (node is not null)
                            node.LastError = null;
                        s.LastError = null;
                    }, ct);
                    refreshed++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Faker bot token refresh failed for {Bot} on {Content}", bot.Username, contentUrl);
                }
            }

            if (snapshot.Item2.Length > 0 && refreshed == 0)
            {
                await _store.UpdateAsync(s =>
                {
                    var node = s.FindNode(contentUrl);
                    if (node is not null)
                        node.LastError = "Bot sessions expired. Reconnect, then Create bots if this does not clear.";
                    s.LastError = node?.LastError;
                }, ct);
            }

            return refreshed;
        }
        finally
        {
            _gate.Release();
        }
    }
}

public class FakerPostingHostedService : BackgroundService
{
    private static readonly string[] Replies =
    [
        "That tracks.",
        "Anyone else seeing this on staging?",
        "Looping back after a coffee.",
        "I can reproduce on a second pass.",
        "Not blocking, just noting it.",
        "Ship it and we can iterate."
    ];

    private readonly FakerStore _store;
    private readonly FakerClient _client;
    private readonly FakerTokenService _tokens;
    private readonly ILogger<FakerPostingHostedService> _logger;

    public FakerPostingHostedService(
        FakerStore store,
        FakerClient client,
        FakerTokenService tokens,
        ILogger<FakerPostingHostedService> logger)
    {
        _store = store;
        _client = client;
        _tokens = tokens;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = await _store.ReadAsync(s => Math.Clamp(s.IntervalSeconds, 2, 60), stoppingToken);
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delay), stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }

            var ready = await _store.ReadAsync(s =>
            {
                if (!s.Posting)
                    return Array.Empty<FakerNodeState>();
                return s.Nodes.Where(n => n.ReadyToPost).Select(CloneForTick).ToArray();
            }, stoppingToken);

            if (ready.Length == 0)
            {
                await _store.UpdateAsync(s =>
                {
                    if (s.Posting && !s.Nodes.Any(n => n.ReadyToPost))
                        s.LastError = "Fill a connected node first so bots have nests to post in.";
                }, stoppingToken);
                continue;
            }

            foreach (var node in ready)
            {
                try
                {
                    await ApplyTickAsync(node, retried: false, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Faker keep-posting tick failed for {Content}", node.ContentBaseUrl);
                    await _store.UpdateAsync(s =>
                    {
                        var live = s.FindNode(node.ContentBaseUrl);
                        if (live is not null)
                            live.LastError = ex.Message;
                        s.LastError = ex.Message;
                    }, stoppingToken);
                }
            }
        }
    }

    private async Task ApplyTickAsync(FakerNodeState node, bool retried, CancellationToken ct)
    {
        await _tokens.RefreshBotsAsync(node.ContentBaseUrl, force: retried, ct);
        var fresh = await _store.ReadAsync(s =>
        {
            var live = s.FindNode(node.ContentBaseUrl);
            return live is null ? null : CloneForTick(live);
        }, ct);
        if (fresh is null || !fresh.ReadyToPost)
            return;
        if (fresh.Bots.All(b => FakerTargets.IsExpiring(b.AccessExpiresAt, b.AccessToken)))
            return;

        try
        {
            var tick = await TickNodeAsync(fresh, ct);
            await _store.UpdateAsync(s =>
            {
                var live = s.FindNode(tick.ContentBaseUrl);
                if (live is null)
                    return;
                if (tick.NewThreadId is { } threadId)
                {
                    live.ThreadIds.Add(threadId);
                    if (!live.PostIds.Contains(threadId))
                        live.PostIds.Add(threadId);
                }
                if (tick.NewPostId is { } postId && !live.PostIds.Contains(postId))
                    live.PostIds.Add(postId);
                foreach (var key in tick.UpvotedKeys)
                {
                    if (!live.UpvotedKeys.Contains(key))
                        live.UpvotedKeys.Add(key);
                }
                live.LastAction = tick.LastAction;
                live.LastError = null;
                s.LastAction = tick.LastAction;
                s.LastError = null;
            }, ct);
        }
        catch (Exception ex) when (!retried && FakerTargets.IsUnauthorized(ex))
        {
            _logger.LogInformation("Refreshing bot tokens after 401 on {Content}", node.ContentBaseUrl);
            await ApplyTickAsync(node, retried: true, ct);
        }
    }

    private static FakerNodeState CloneForTick(FakerNodeState node) => new()
    {
        ContentBaseUrl = node.ContentBaseUrl,
        Bots = [.. node.Bots],
        ForumIds = [.. node.ForumIds],
        ThreadIds = [.. node.ThreadIds],
        PostIds = [.. node.PostIds],
        UpvotedKeys = [.. node.UpvotedKeys]
    };

    private async Task<TickResult> TickNodeAsync(FakerNodeState node, CancellationToken ct)
    {
        var rng = Random.Shared;
        var bot = node.Bots[rng.Next(node.Bots.Count)];
        var forumId = node.ForumIds[rng.Next(node.ForumIds.Count)];
        var host = new Uri(node.ContentBaseUrl).Host + ":" + new Uri(node.ContentBaseUrl).Port;
        var hotThreads = await LoadHotThreadTargetsAsync(node, bot.AccessToken, ct);
        var replyTargets = hotThreads.Where(t => t.ReplyCount == 0).ToList();
        if (replyTargets.Count == 0)
            replyTargets = hotThreads.ToList();

        if (replyTargets.Count > 0 && rng.NextDouble() < 0.65)
        {
            var target = replyTargets[rng.Next(replyTargets.Count)];
            var existingReplyId = await TryPostReplyAsync(node, target.ForumId, target.Id, bot, rng, ct);
            if (existingReplyId is { } posted)
            {
                var replyKeys = await UpvotePostAsync(node, posted, 3, bot, rng, ct);
                var replyExtra = replyKeys.Count == 0 ? "" : $" ({replyKeys.Count} upvotes)";
                return new TickResult(
                    node.ContentBaseUrl,
                    $"{bot.DisplayName} replied on {host}{replyExtra}",
                    null,
                    posted,
                    replyKeys);
            }
        }

        if (node.ThreadIds.Count > 0 && rng.NextDouble() < 0.45)
        {
            var upvote = await TryUpvoteRecentPostAsync(node, host, rng, ct);
            if (upvote is not null)
                return upvote;
        }

        var created = await _client.PostContentAsync(node.ContentBaseUrl, "/api/posts", new
        {
            forumId,
            parentPostId = (Guid?)null,
            title = $"Live note {DateTime.UtcNow:HH:mm:ss}",
            message = "Keep-posting tick from Faker so SignalR has something to chew on."
        }, bot.AccessToken, ct);
        var newId = FakerTargets.ReadId(created);
        Guid? replyId = null;
        var replier = OtherBot(node, bot, rng);
        if (replier is not null)
            replyId = await TryPostReplyAsync(node, forumId, newId, replier, rng, ct);

        var keys = await UpvotePostAsync(node, newId, 4, bot, rng, ct);
        if (replyId is { } postedReply)
            keys.AddRange(await UpvotePostAsync(node, postedReply, 3, replier, rng, ct));

        var extra = keys.Count == 0 ? "" : $" ({keys.Count} upvotes)";
        var replyNote = replyId is null ? "" : ", 1 reply";
        return new TickResult(
            node.ContentBaseUrl,
            $"{bot.DisplayName} started a thread on {host}{extra}{replyNote}",
            newId,
            replyId ?? newId,
            keys);
    }

    private async Task<IReadOnlyList<(Guid Id, Guid ForumId, int ReplyCount)>> LoadHotThreadTargetsAsync(
        FakerNodeState node,
        string bearer,
        CancellationToken ct)
    {
        try
        {
            var json = await _client.GetContentAsync(
                node.ContentBaseUrl,
                "/api/posts/recent?sort=hot&page=1&pageSize=10",
                bearer,
                ct);
            if (json.ValueKind != JsonValueKind.Array)
                return [];

            var targets = new List<(Guid Id, Guid ForumId, int ReplyCount)>();
            foreach (var item in json.EnumerateArray())
            {
                if (!item.TryGetProperty("post", out var post))
                    continue;
                if (!TryReadGuid(post, "id", out var id) || !TryReadGuid(post, "forumId", out var postForumId))
                    continue;
                var replies = 0;
                if (post.TryGetProperty("replyCount", out var replyCount) && replyCount.TryGetInt32(out var parsed))
                    replies = parsed;
                targets.Add((id, postForumId, replies));
            }

            return targets;
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static bool TryReadGuid(JsonElement obj, string name, out Guid value)
    {
        value = Guid.Empty;
        if (!obj.TryGetProperty(name, out var raw))
            return false;
        if (raw.TryGetGuid(out value))
            return true;
        return raw.ValueKind == JsonValueKind.String && Guid.TryParse(raw.GetString(), out value);
    }

    private async Task<Guid?> TryPostReplyAsync(
        FakerNodeState node,
        Guid forumId,
        Guid threadId,
        FakerBotState bot,
        Random rng,
        CancellationToken ct)
    {
        try
        {
            return await PostReplyAsync(node, forumId, threadId, bot, rng, ct);
        }
        catch (InvalidOperationException ex) when (IsRateLimited(ex))
        {
            return null;
        }
    }

    private async Task<Guid> PostReplyAsync(
        FakerNodeState node,
        Guid forumId,
        Guid threadId,
        FakerBotState bot,
        Random rng,
        CancellationToken ct)
    {
        var reply = await _client.PostContentAsync(node.ContentBaseUrl, "/api/posts", new
        {
            forumId,
            parentPostId = threadId,
            message = Replies[rng.Next(Replies.Length)]
        }, bot.AccessToken, ct);
        return FakerTargets.ReadId(reply);
    }

    private static FakerBotState? OtherBot(FakerNodeState node, FakerBotState skip, Random rng)
    {
        var others = node.Bots
            .Where(b => !string.Equals(b.Username, skip.Username, StringComparison.Ordinal))
            .ToList();
        if (others.Count == 0)
            return null;
        return others[rng.Next(others.Count)];
    }

    private async Task<TickResult?> TryUpvoteRecentPostAsync(
        FakerNodeState node,
        string host,
        Random rng,
        CancellationToken ct)
    {
        var recentThreads = RecentIds(node.ThreadIds, 8);
        var recentPosts = RecentIds(node.PostIds, 12);
        var pool = recentThreads.Concat(recentPosts).Distinct().ToList();
        if (pool.Count == 0)
            return null;

        var postId = pool[rng.Next(pool.Count)];
        var keys = await UpvotePostAsync(node, postId, 3, null, rng, ct);
        if (keys.Count == 0)
            return null;

        var kind = node.ThreadIds.Contains(postId) ? "thread" : "reply";
        return new TickResult(
            node.ContentBaseUrl,
            $"{keys.Count} bot upvote(s) on a {kind} on {host}",
            null,
            null,
            keys);
    }

    private async Task<List<string>> UpvotePostAsync(
        FakerNodeState node,
        Guid postId,
        int maxVoters,
        FakerBotState? skip,
        Random rng,
        CancellationToken ct)
    {
        var voted = node.UpvotedKeys.ToHashSet(StringComparer.Ordinal);
        var voters = node.Bots
            .Where(b => skip is null || !string.Equals(b.Username, skip.Username, StringComparison.Ordinal))
            .Where(b => !voted.Contains(FakerTargets.VoteKey(b.Username, postId)))
            .OrderBy(_ => rng.Next())
            .Take(Math.Max(1, maxVoters))
            .ToList();

        var keys = new List<string>();
        foreach (var voter in voters)
        {
            JsonElement result;
            try
            {
                result = await _client.PostContentAsync(
                    node.ContentBaseUrl,
                    $"/api/posts/{postId:D}/upvote",
                    null,
                    voter.AccessToken,
                    ct);
            }
            catch (InvalidOperationException ex) when (IsRateLimited(ex) || IsSelfUpvote(ex))
            {
                if (IsSelfUpvote(ex))
                    continue;
                break;
            }

            var upvoted = result.ValueKind == JsonValueKind.Object
                          && result.TryGetProperty("viewerHasUpvoted", out var flag)
                          && flag.GetBoolean();
            if (!upvoted)
                continue;

            var key = FakerTargets.VoteKey(voter.Username, postId);
            keys.Add(key);
            node.UpvotedKeys.Add(key);
        }

        return keys;
    }

    private static bool IsRateLimited(InvalidOperationException ex) =>
        ex.Message.Contains("Rate limit", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("(429)", StringComparison.Ordinal);

    private static bool IsSelfUpvote(InvalidOperationException ex) =>
        ex.Message.Contains("cannot upvote your own post", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<Guid> RecentIds(IReadOnlyList<Guid> ids, int take)
    {
        if (ids.Count == 0)
            return [];
        if (ids.Count <= take)
            return ids;
        return ids.Skip(ids.Count - take).ToList();
    }

    private sealed record TickResult(
        string ContentBaseUrl,
        string LastAction,
        Guid? NewThreadId,
        Guid? NewPostId,
        IReadOnlyList<string> UpvotedKeys);
}
