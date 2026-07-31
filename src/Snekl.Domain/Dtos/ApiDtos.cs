namespace Snekl.Domain.Dtos;

public record NodeInfoDto(
    Guid Id,
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

public record CreateForumRequest(string Name, string Slug, string Description);
public record ForumDto(
    Guid Id,
    string Name,
    string Slug,
    string Description,
    UserDto? CreatedBy,
    string? OriginNodeUrl,
    bool IsFederated,
    int PostCount,
    DateTime CreatedAt);

public record CreatePostRequest(Guid ForumId, Guid? ParentPostId, string Message);
public record PostDto(
    Guid Id,
    Guid ForumId,
    Guid? ParentPostId,
    UserDto Author,
    string Message,
    int ReplyCount,
    string? OriginNodeUrl,
    bool IsFederated,
    DateTime CreatedAt,
    List<PostDto> Replies);

public record SubscribePeerRequest(string BaseUrl);
public record PeerDto(Guid Id, string BaseUrl, string Name, Guid RemoteNodeId, bool IsActive, DateTime SubscribedAt);

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
