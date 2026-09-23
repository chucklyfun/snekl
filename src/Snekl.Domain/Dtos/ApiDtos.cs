namespace Snekl.Domain.Dtos;

public record RateLimitRuleDto(
    string PathPrefix,
    string Method,
    int Limit,
    int WindowSeconds);

public record RateLimitPolicyDto(
    bool Enabled,
    string Partition,
    IReadOnlyList<RateLimitRuleDto> Rules);

public record NodeInfoDto(
    Guid Id,
    string Did,
    string Name,
    string BaseUrl,
    string Description,
    DateTime CreatedAt,
    string? HubBaseUrl = null,
    RateLimitPolicyDto? RateLimiting = null,
    string? OwnerUsername = null,
    Guid? OwnerUserId = null);

public record RegisterRequest(string Username, string Email, string Password, string DisplayName);
public record LoginRequest(string Username, string Password);
public record ReauthenticateRequest(string Password);
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
public record ForumSummaryDto(Guid Id, string Name, string Slug, int PostCount, string? AvatarUrl = null);
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
    bool AllowAliases = true,
    string ApprovalStatus = "approved",
    bool ViewerCanApprove = false,
    bool ViewerIsMember = false,
    bool ViewerCanManage = false,
    bool ViewerIsFavorite = false,
    IReadOnlyList<WatercoolerSummaryDto>? Watercoolers = null,
    IReadOnlyList<ForumContentBlockDto>? ContentBlocks = null,
    bool NsfwAgeCheckRequired = false,
    bool NsfwAgeCheckRequiredByServer = false,
    bool NsfwAgeCheckEnforced = false,
    int NsfwMinimumAge = 18,
    bool ViewerNsfwAgeAttested = false,
    string? AvatarUrl = null,
    string? BannerUrl = null,
    string? NsfwAgeCheckExplanation = null,
    bool NsfwAgeCheckBlocksEntry = false,
    string? NsfwAgeCheckGateExplanation = null,
    string? OwnerUsername = null,
    bool IsViewerOwner = false);

public record SetForumFavoriteRequest(bool IsFavorite);

public record CreateWatercoolerRequest(
    string Name,
    string Slug,
    string Description,
    Guid? ParentForumId = null,
    bool? AllowUpvotes = null,
    bool? NewestAtBottom = null);

public record UpdateWatercoolerRequest(
    string? Name = null,
    string? Description = null,
    bool? AllowUpvotes = null,
    bool? NewestAtBottom = null);

public record WatercoolerSummaryDto(
    Guid Id,
    string Name,
    string Slug,
    int PostCount,
    bool AllowUpvotes,
    bool NewestAtBottom,
    string ApprovalStatus,
    bool ViewerCanView,
    bool ViewerCanSpeak = false);

public record WatercoolerDto(
    Guid Id,
    string Name,
    string Slug,
    string Description,
    UserPresentationDto? CreatedBy,
    Guid? ParentForumId,
    string? ParentForumSlug,
    string? ParentForumName,
    bool AllowUpvotes,
    bool NewestAtBottom,
    string ApprovalStatus,
    DateTime CreatedAt,
    int PostCount,
    IReadOnlyList<string>? ViewerPermissions = null,
    bool ViewerCanApprove = false,
    bool ViewerCanManage = false);

public record WatercoolerRoleDto(
    Guid Id,
    string Name,
    string? Color,
    int Position,
    IReadOnlyList<string> Permissions,
    bool IsEveryone);

public record CreateWatercoolerRoleRequest(
    string Name,
    string? Color,
    int Position,
    IReadOnlyList<string> Permissions);

public record UpdateWatercoolerRoleRequest(
    string Name,
    string? Color,
    int Position,
    IReadOnlyList<string> Permissions);

public record ForumContentBlockDto(
    Guid Id,
    string TypeKey,
    Guid? WatercoolerId,
    string Slot,
    int Position);

public record UpsertForumContentBlocksRequest(IReadOnlyList<ForumContentBlockDto> Blocks);

public record CreatePostRequest(
    Guid? ForumId,
    Guid? ParentPostId,
    string Message,
    string? Title = null,
    IReadOnlyList<Guid>? MediaAssetIds = null,
    Guid? WatercoolerId = null,
    Guid? QuotedPostId = null,
    IReadOnlyList<NsfwMediaHintDto>? NsfwMedia = null,
    string? ReplyPolicy = null);

public record NsfwMediaHintDto(Guid MediaAssetId, string CoverText);

public record UpdatePostMediaNsfwRequest(bool IsNsfw, string? CoverText = null);
public record UpdatePostRequest(string Message);

public record UpdateReplyPolicyRequest(string ReplyPolicy);

public record MediaAttachmentDto(
    Guid Id,
    string? Url,
    string ContentType,
    long ByteSize,
    int? Width = null,
    int? Height = null,
    bool IsNsfw = false,
    string? CoverText = null,
    string? CensorUrl = null);

public record TopReplyPreviewDto(
    Guid Id,
    UserPresentationDto Author,
    string Message,
    int UpvoteCount,
    DateTime CreatedAt);

/// <summary>
/// Lightweight SignalR payload for live thread/forum updates.
/// Types: post.created, post.updated, post.deleted, post.hardDeleted, post.upvoted, post.reacted, thread.locked, thread.pinned.
/// </summary>
public record RealtimePostEvent(
    string Type,
    Guid ThreadId,
    Guid? ForumId,
    Guid PostId,
    Guid? ParentPostId = null,
    int? UpvoteCount = null,
    bool? IsLocked = null,
    bool? IsPinned = null,
    bool? IsDeleted = null,
    Guid? WatercoolerId = null);

public record PostViewerActionsDto(
    bool CanEdit,
    bool CanSoftDelete,
    bool CanHardDelete,
    bool CanViewHistory,
    bool CanLock = false,
    bool CanUnlock = false,
    bool CanPin = false,
    bool CanUnpin = false,
    bool CanReport = false,
    bool CanMarkNsfwMedia = false,
    bool CanReply = true,
    bool CanSetReplyPolicy = false,
    bool CanUpvote = true);

public record PostRevisionDto(
    Guid Id,
    string Message,
    Guid EditedByUserId,
    string EditedByUsername,
    DateTime CreatedAt);

public record QuotedPostDto(
    Guid Id,
    UserPresentationDto Author,
    string Message,
    Guid WatercoolerId,
    DateTime CreatedAt,
    bool ViewerCanView);

public record PostDto(
    Guid Id,
    Guid? ForumId,
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
    PostViewerActionsDto? ViewerActions = null,
    string? Title = null,
    IReadOnlyList<MediaAttachmentDto>? Attachments = null,
    TopReplyPreviewDto? TopReplyPreview = null,
    IReadOnlyList<PostReactionDto>? Reactions = null,
    Guid? WatercoolerId = null,
    Guid? QuotedPostId = null,
    int QuoteCount = 0,
    QuotedPostDto? QuotedPost = null,
    IReadOnlyList<PostDto>? Ancestors = null,
    string ReplyPolicy = "everyone");

public record EmoteDto(
    Guid Id,
    string Code,
    string Name,
    string ImageUrl,
    Guid? ForumId,
    bool IsAnimated,
    DateTime CreatedAt,
    string? SkinsUnicode = null,
    string? ForumSlug = null,
    string? ForumName = null,
    string? ServerDid = null,
    string? ServerPrefix = null,
    bool CanInsert = true,
    int SortOrder = 0,
    bool Highlighted = false);

public record CreateEmoteRequest(
    string Code,
    Guid? MediaAssetId = null,
    Guid? ForumId = null,
    string? Name = null,
    string? SkinsUnicode = null,
    bool Highlighted = false);

public sealed record UpdateEmoteRequest
{
    public string? Name { get; init; }
    public string? Code { get; init; }
    public Guid? MediaAssetId { get; init; }
    public string? SkinsUnicode { get; init; }
    public bool ClearSkin { get; init; }
    public bool? Highlighted { get; init; }
}

public record ReorderEmotesRequest(Guid[] Ids, Guid? ForumId = null);

public record ToggleReactionRequest(Guid? EmoteId = null, string? Emoji = null);

public record PostReactionDto(
    string Key,
    Guid? EmoteId,
    string? Code,
    string? Emoji,
    string? ImageUrl,
    int Count,
    bool ViewerReacted);

public record UpvoteResultDto(int UpvoteCount, bool ViewerHasUpvoted);

public record RecentThreadDto(
    PostDto Post,
    string ForumSlug,
    string ForumName,
    string? ParentForumSlug,
    string OwnerBaseUrl,
    string OwnerDid);

public record SearchHitDto(
    RecentThreadDto Thread,
    string Snippet,
    bool MatchedInReply);

public record SubscribePeerRequest(string BaseUrl);
public record PeerDto(Guid Id, string BaseUrl, string Name, Guid RemoteNodeId, bool IsActive, DateTime SubscribedAt);

public record NetworkServerDto(
    string Did,
    Guid NodeId,
    string BaseUrl,
    string Name,
    bool IsSelf);

public static class OperatorStates
{
    public const string Unclaimed = "unclaimed";
    public const string SetupRequired = "setupRequired";
    public const string Ready = "ready";
    public const string Forbidden = "forbidden";
}

public record SiteAdminDto(
    Guid Id,
    Guid UserId,
    string Username,
    string? Did,
    bool HasPassword,
    DateTime CreatedAt);

public record SiteAdminAuthResponse(string Token, SiteAdminDto Admin);
public record OperatorStatusDto(string State);
public record OperatorLoginRequest(string? AdminPassword = null, string? NewAdminPassword = null);
public record InviteOperatorRequest(string Username);
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
    DateTime CreatedAt,
    string? Title = null,
    IReadOnlyList<string>? AttachmentUrls = null);

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
    IReadOnlyList<string> ServerPermissions,
    bool IsServerOwner = false);

public record SuperUserDto(
    Guid UserId,
    string Username,
    string DisplayName,
    Guid? GrantedBySiteAdminId,
    DateTime GrantedAt,
    string? Note);

public record GrantSuperUserRequest(string Username, string? Note = null);

public record NodeAuthInfoDto(IReadOnlyList<TrustedAuthIssuerDto> Issuers);
public record TrustedAuthIssuerDto(string Name, string IssuerUrl, string JwksUrl);

public record AdminBootstrapDto(
    string Kind,
    string Name,
    string PublicBaseUrl,
    string? Did,
    string AuthLoginUrl);

public record CreateHubInviteRequest(string HubBaseUrl, string HubName);
public record HubInviteDto(
    Guid Id,
    string HubBaseUrl,
    string HubName,
    string RequestedByDid,
    string RequestedByUsername,
    string Status,
    DateTime CreatedAt);

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
    IReadOnlyList<string> Permissions,
    IReadOnlyList<ServerRoleDto> Roles,
    bool NsfwAgeCheckRequired = false,
    int NsfwMinimumAge = 18,
    bool ViewerNsfwAgeAttested = false,
    bool IsServerOwner = false);

public record ChangeLogEntryDto(
    Guid Id,
    string Scope,
    Guid? ForumId,
    Guid? WatercoolerId,
    string EntityType,
    Guid EntityId,
    string Action,
    Guid? ActorUserId,
    string ActorUsername,
    string Summary,
    string? BeforeJson,
    string? AfterJson,
    DateTime CreatedAt);

public record NsfwAgeCheckStatusDto(
    bool Enforced,
    bool RequiredByServer,
    bool RequiredByForum,
    int MinimumAge,
    bool ViewerAttested,
    string? ForumExplanation = null,
    bool BlocksEntry = false);

public record SubmitNsfwAgeCheckRequest(DateOnly DateOfBirth, Guid? ForumId = null);

public record UpdateServerNsfwAgeCheckRequest(bool Required, int? MinimumAge = null);

public record ForumRoleDto(
    Guid Id,
    Guid ForumId,
    string Name,
    string? Color,
    int Position,
    IReadOnlyList<string> Permissions,
    bool IsEveryone,
    Guid? InheritFromRoleId,
    bool IsOwner = false);

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

public record UpdateForumFlairTemplateRequest(
    string? Emoji,
    string? IconUrl,
    string? Label,
    string? NameColor,
    string? NameStyle,
    int? Position = null);

public record AssignForumFlairRequest(Guid? TemplateId);

public record SetForumAliasRequest(string Alias);

public record UpdateForumSettingsRequest(
    string? Name,
    string? Description,
    bool? AllowAliases,
    bool? NsfwAgeCheckRequired = null,
    string? NsfwAgeCheckExplanation = null,
    Guid? AvatarMediaId = null,
    Guid? BannerMediaId = null,
    bool ClearAvatar = false,
    bool ClearBanner = false);

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

public record ExtensionAppPublicDto(
    string ClientId,
    string Name,
    string? AdminUrl,
    string? IframeUrl,
    IReadOnlyList<string> RedirectUris);

public record CreateExtensionInstallRequest(
    string ClientId,
    string AppName,
    string? AdminUrl,
    string? IframeUrl,
    string ResourceType,
    Guid? ForumId,
    IReadOnlyList<string> ServerPermissions,
    IReadOnlyList<string> ForumPermissions);

public record ExtensionInstallDto(
    Guid Id,
    string ClientId,
    string AppName,
    string? AdminUrl,
    string? IframeUrl,
    string ResourceType,
    Guid? ForumId,
    string? ForumName,
    IReadOnlyList<string> ServerPermissions,
    IReadOnlyList<string> ForumPermissions,
    Guid InstalledByUserId,
    string InstalledByDid,
    DateTime CreatedAt,
    RateLimitPolicyDto? RateLimiting = null);

public record ExtensionInstallOptionForumDto(
    Guid Id,
    string Name,
    string Slug,
    bool CanInstall,
    IReadOnlyList<string> ForumPermissions);

public record ExtensionInstallOptionsDto(
    bool CanInstallServer,
    IReadOnlyList<string> ServerPermissions,
    IReadOnlyList<ExtensionInstallOptionForumDto> Forums,
    ExtensionInstallDto? ExistingInstall = null);

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

public record ContentBlockPermissionDto(
    string Name,
    string Label,
    string? Description,
    string Scope);

public record ContentBlockTypeDto(
    string Key,
    string Name,
    string? Description,
    IReadOnlyList<ContentBlockPermissionDto> Permissions);

public record DirectMessagePeerDto(
    Guid Id,
    string Username,
    string DisplayName,
    string? Did);

public record DirectMessageForumOptionDto(
    Guid Id,
    string Name,
    string Slug,
    string? ParentSlug);

public record CreateDirectConversationRequestDto(
    string TargetUsername,
    Guid? ForumId = null,
    string? ForumSlug = null,
    string? ParentSlug = null,
    string? Message = null);

public record DirectConversationRequestDto(
    Guid Id,
    DirectMessagePeerDto Requester,
    DirectMessagePeerDto Target,
    DirectMessageForumOptionDto Forum,
    string? Message,
    string Status,
    DateTime CreatedAt,
    DateTime? ResolvedAt,
    Guid? ConversationId);

public record DirectConversationSummaryDto(
    Guid Id,
    DirectMessagePeerDto Peer,
    DirectMessageForumOptionDto? OriginForum,
    string? LastMessagePreview,
    DateTime UpdatedAt);

public record DirectMessageDto(
    Guid Id,
    Guid ConversationId,
    DirectMessagePeerDto Author,
    string Body,
    DateTime CreatedAt);

public record DirectConversationDto(
    Guid Id,
    DirectMessagePeerDto Peer,
    DirectMessageForumOptionDto? OriginForum,
    DateTime CreatedAt,
    IReadOnlyList<DirectMessageDto> Messages);

public record SendDirectMessageRequest(string Body);

public record DirectMessageEligibilityDto(
    DirectMessagePeerDto Target,
    bool CanRequest,
    string? Reason,
    Guid? ExistingConversationId,
    DirectConversationRequestDto? PendingRequest,
    IReadOnlyList<DirectMessageForumOptionDto> EligibleForums);

public record DirectMessageInboxDto(
    IReadOnlyList<DirectConversationRequestDto> IncomingRequests,
    IReadOnlyList<DirectConversationRequestDto> OutgoingRequests,
    IReadOnlyList<DirectConversationSummaryDto> Conversations);

public record SetDirectMessageForumPreferenceRequest(string Mode);

public record DirectMessageForumPreferenceDto(
    DirectMessageForumOptionDto Forum,
    string Mode);

public record DirectMessageForumPreferencesDto(
    IReadOnlyList<DirectMessageForumPreferenceDto> Forums);
