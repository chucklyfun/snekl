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

public record CreateForumRequest(string Name, string Slug, string Description, Guid? ParentForumId = null);
public record ForumSummaryDto(Guid Id, string Name, string Slug, int PostCount);
public record ForumDto(
    Guid Id,
    string Name,
    string Slug,
    string Description,
    UserPresentationDto? CreatedBy,
    string? OriginNodeUrl,
    bool IsFederated,
    int PostCount,
    DateTime CreatedAt,
    Guid? ParentForumId = null,
    string? ParentSlug = null,
    IReadOnlyList<ForumSummaryDto>? Subforums = null,
    IReadOnlyList<string>? ViewerPermissions = null,
    bool AllowAliases = true);

public record CreatePostRequest(Guid ForumId, Guid? ParentPostId, string Message);
public record UpdatePostRequest(string Message);

public record PostViewerActionsDto(
    bool CanEdit,
    bool CanSoftDelete,
    bool CanHardDelete,
    bool CanViewHistory,
    bool CanLock = false,
    bool CanUnlock = false,
    bool CanPin = false,
    bool CanUnpin = false,
    bool CanReport = false);

public record PostRevisionDto(
    Guid Id,
    string Message,
    Guid EditedByUserId,
    string EditedByUsername,
    DateTime CreatedAt);

public record PostDto(
    Guid Id,
    Guid ForumId,
    Guid? ParentPostId,
    UserPresentationDto Author,
    string Message,
    int ReplyCount,
    int UpvoteCount,
    bool ViewerHasUpvoted,
    string? OriginNodeUrl,
    bool IsFederated,
    bool IsDeleted,
    bool IsLocked,
    bool IsPinned,
    DateTime? EditedAt,
    DateTime CreatedAt,
    List<PostDto> Replies,
    PostViewerActionsDto? ViewerActions = null);

public record UpvoteResultDto(int UpvoteCount, bool ViewerHasUpvoted);

public record RecentThreadDto(
    PostDto Post,
    string ForumSlug,
    string ForumName,
    string? ParentForumSlug,
    string OwnerBaseUrl,
    string OwnerDid);

public record SubscribePeerRequest(string BaseUrl);
public record PeerDto(Guid Id, string BaseUrl, string Name, Guid RemoteNodeId, bool IsActive, DateTime SubscribedAt);

public record NetworkServerDto(
    string Did,
    Guid NodeId,
    string BaseUrl,
    string Name,
    bool IsSelf);

public record SiteAdminDto(Guid Id, string Username, DateTime CreatedAt);
public record SiteAdminAuthResponse(string Token, SiteAdminDto Admin);
public record UpdateNodeRequest(string Name, string BaseUrl, string Description);

public record FederationEnvelope(
    string Type,
    Guid SourceNodeId,
    string SourceNodeUrl,
    DateTime Timestamp,
    object Payload,
    string? Signature = null);

public record FederatedPostPayload(
    Guid Id,
    Guid ForumId,
    string ForumSlug,
    string ForumName,
    Guid? ParentPostId,
    FederatedUserPayload Author,
    string Message,
    DateTime CreatedAt);

public record FederatedPostUpdatePayload(
    Guid Id,
    string Message,
    DateTime EditedAt);

public record FederatedPostDeletePayload(
    Guid Id,
    DateTime DeletedAt);

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

public record AuthMeResponse(
    UserPresentationDto User,
    bool IsSuperUser,
    IReadOnlyList<string> ServerPermissions);

public record SuperUserDto(
    Guid UserId,
    string Username,
    string DisplayName,
    Guid GrantedBySiteAdminId,
    DateTime GrantedAt,
    string? Note);

public record GrantSuperUserRequest(string Username, string? Note = null);

public record ServerRoleDto(
    Guid Id,
    string Name,
    string? Color,
    int Position,
    IReadOnlyList<string> Permissions,
    bool IsEveryone);

public record CreateServerRoleRequest(
    string Name,
    string? Color,
    int Position,
    IReadOnlyList<string> Permissions);

public record UpdateServerRoleRequest(
    string Name,
    string? Color,
    int Position,
    IReadOnlyList<string> Permissions);

public record ServerMemberDto(
    Guid UserId,
    string Username,
    string DisplayName,
    IReadOnlyList<ServerRoleDto> Roles);

public record AssignServerRolesRequest(IReadOnlyList<Guid> RoleIds, string? Username = null);

public record ServerPermissionsDto(
    bool IsSuperUser,
    IReadOnlyList<string> Permissions);

public record ForumRoleDto(
    Guid Id,
    Guid ForumId,
    string Name,
    string? Color,
    int Position,
    IReadOnlyList<string> Permissions,
    bool IsEveryone,
    Guid? InheritFromRoleId);

public record CreateForumRoleRequest(
    string Name,
    string? Color,
    int Position,
    IReadOnlyList<string> Permissions);

public record UpdateForumRoleRequest(
    string Name,
    string? Color,
    int Position,
    IReadOnlyList<string> Permissions);

public record ForumMemberDto(
    Guid UserId,
    string Username,
    string DisplayName,
    IReadOnlyList<ForumRoleDto> Roles,
    ForumFlairTemplateDto? Flair,
    string? Alias);

public record AssignForumRolesRequest(IReadOnlyList<Guid> RoleIds);

public record ForumFlairTemplateDto(
    Guid Id,
    Guid ForumId,
    string? Emoji,
    string? IconUrl,
    string? Label,
    string? NameColor,
    string? NameStyle,
    int Position,
    Guid? InheritFromTemplateId);

public record CreateForumFlairTemplateRequest(
    string? Emoji,
    string? IconUrl,
    string? Label,
    string? NameColor,
    string? NameStyle,
    int Position);

public record AssignForumFlairRequest(Guid? TemplateId);

public record SetForumAliasRequest(string Alias);

public record UpdateForumSettingsRequest(
    string? Name,
    string? Description,
    bool? AllowAliases);

public record BanForumMemberRequest(string Username, string? Reason, DateTime? ExpiresAt);

public record ForumBanDto(
    Guid Id,
    Guid UserId,
    string Username,
    string DisplayName,
    string? Reason,
    DateTime? ExpiresAt,
    string BannedByUsername,
    DateTime CreatedAt);

public record ReportPostRequest(string Reason);

public record PostReportDto(
    Guid Id,
    Guid PostId,
    string PostPreview,
    string PostAuthorUsername,
    Guid ReporterUserId,
    string ReporterUsername,
    string Reason,
    string Status,
    DateTime CreatedAt,
    DateTime? ResolvedAt);
