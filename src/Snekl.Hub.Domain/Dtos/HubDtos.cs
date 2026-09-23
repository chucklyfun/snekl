namespace Snekl.Hub.Domain.Dtos;

public record HubInfoDto(
    string Name,
    string BaseUrl,
    string AuthBaseUrl,
    long MapVersion);

public record FederationAuthConfigDto(string Issuer, string Jwks);

public record FederationConfigServerDto(string Did, string BaseUrl, string Name);

public record FederationConfigDto(
    string Schema,
    string Name,
    string BaseUrl,
    FederationAuthConfigDto Auth,
    long MapVersion,
    IReadOnlyList<FederationConfigServerDto> Servers,
    string? Feed);

public static class FederationConfigSchema
{
    public const string V1 = "snekl-federation/1";
}

public record AdminBootstrapDto(
    string Kind,
    string Name,
    string PublicBaseUrl,
    string? Did,
    string AuthLoginUrl);

public record RegisteredServerDto(
    string ServerDid,
    string BaseUrl,
    string Name,
    DateTime RegisteredAt,
    long AckedMapVersion,
    string Status = "active",
    string? InitiatedBy = null,
    Guid? OriginInviteId = null);

public record FederationMapDto(
    long MapVersion,
    IReadOnlyList<RegisteredServerDto> Servers);

public record RegisterServerRequest(
    string ServerDid,
    string BaseUrl,
    string Name,
    string? InitiatedBy = null,
    Guid? OriginInviteId = null);

public record AcceptJoinRequest(
    string ServerDid,
    string AcceptedBy,
    Guid? OriginInviteId = null);

public record DeclineJoinRequest(string ServerDid);

public record MapAckRequest(
    string ServerDid,
    long MapVersion);

public record HubFeedCardDto(
    int SchemaVersion,
    Guid PostId,
    Guid ForumId,
    string ForumSlug,
    string ForumName,
    string ServerDid,
    string OriginBaseUrl,
    string OriginPostUrl,
    string? Title,
    string Excerpt,
    string? ImagePreviewUrl,
    string AuthorDid,
    string AuthorDisplayName,
    DateTime CreatedAt,
    DateTime? EditedAt,
    string Visibility,
    int Score,
    int CommentCount,
    long MapVersion,
    string Event,
    DateTime EventAt,
    string? NsfwCover = null);

public record HubSearchHitDto(HubFeedCardDto Thread, string Snippet);

public record HubScorePatchDto(
    int SchemaVersion,
    Guid PostId,
    string ServerDid,
    int Score,
    int CommentCount,
    DateTime PatchedAt);

public record HubForumMembershipDto(
    int SchemaVersion,
    string UserDid,
    string ServerDid,
    Guid ForumId,
    string Event,
    DateTime EventAt,
    bool IsFavorite = false);

public static class HubOperatorStates
{
    public const string Unclaimed = "unclaimed";
    public const string SetupRequired = "setupRequired";
    public const string Ready = "ready";
    public const string Forbidden = "forbidden";
}

public record HubOperatorStatusDto(string State);
public record HubOperatorLoginRequest(string? AdminPassword = null, string? NewAdminPassword = null);
public record HubOperatorDto(Guid Id, string Did, string Username, bool HasPassword, DateTime CreatedAt);
public record HubAdminAuthResponse(string Token, HubOperatorDto Operator);
public record InviteHubOperatorRequest(string? Username = null, string? Did = null);

public record HubFeedOriginSyncServerDto(
    string ServerDid,
    string BaseUrl,
    string Name,
    int Ingested,
    string? Error);

public record HubFeedSyncResultDto(
    int OriginCount,
    int Ingested,
    IReadOnlyList<HubFeedOriginSyncServerDto> Origins);
