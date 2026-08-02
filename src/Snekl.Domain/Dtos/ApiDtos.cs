namespace Snekl.Domain.Dtos;

public record NodeInfoDto(
    Guid Id,
    string Did,
    string Name,
    string BaseUrl,
    string Description,
    DateTime CreatedAt);

public record RegisterRequest(string Username, string Email, string Password, string DisplayName);
public record LoginRequest(string Username, string Password);
public record AuthResponse(string Token, UserDto User);

public record UserDto(
    Guid Id,
    string Username,
    string DisplayName,
    string Email,
    string Bio,
    string? OriginNodeUrl,
    bool IsFederated);

// Deprecated for display — use UserPresentationDto in posts and profiles.

public record CreateForumRequest(string Name, string Slug, string Description);
public record ForumDto(
    Guid Id,
    string Name,
    string Slug,
    string Description,
    UserPresentationDto? CreatedBy,
    string? OriginNodeUrl,
    bool IsFederated,
    int PostCount,
    DateTime CreatedAt);

public record CreatePostRequest(Guid ForumId, Guid? ParentPostId, string Message);
public record PostDto(
    Guid Id,
    Guid ForumId,
    Guid? ParentPostId,
    UserPresentationDto Author,
    string Message,
    int ReplyCount,
    string? OriginNodeUrl,
    bool IsFederated,
    DateTime CreatedAt,
    List<PostDto> Replies);

public record SubscribePeerRequest(string BaseUrl);
public record PeerDto(Guid Id, string BaseUrl, string Name, Guid RemoteNodeId, bool IsActive, DateTime SubscribedAt);

public record SiteAdminDto(Guid Id, string Username, DateTime CreatedAt);
public record SiteAdminAuthResponse(string Token, SiteAdminDto Admin);
public record UpdateNodeRequest(string Name, string BaseUrl, string Description);

public record FederationEnvelope(
    string Type,
    Guid SourceNodeId,
    string SourceNodeUrl,
    DateTime Timestamp,
    object Payload);

public record FederatedPostPayload(
    Guid Id,
    Guid ForumId,
    string ForumSlug,
    string ForumName,
    Guid? ParentPostId,
    FederatedUserPayload Author,
    string Message,
    DateTime CreatedAt);

public record FederatedUserPayload(
    Guid Id,
    string Username,
    string DisplayName,
    string Bio,
    string OriginNodeUrl);

public record FederatedForumPayload(
    Guid Id,
    string Name,
    string Slug,
    string Description,
    FederatedUserPayload CreatedBy,
    DateTime CreatedAt);

public record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);
