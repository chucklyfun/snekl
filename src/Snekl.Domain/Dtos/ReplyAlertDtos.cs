namespace Snekl.Domain.Dtos;

public record ReplyAlertDto(
    Guid Id,
    Guid? RootPostId,
    string Title,
    int ReplyCount,
    Guid LatestReplyId,
    DateTime LatestReplyAt,
    DateTime CreatedAt,
    DateTime? ReadAt,
    string Kind,
    Guid? ForumId,
    Guid? WatercoolerId,
    string? Slug,
    string? ParentSlug,
    string? ForumParentSlug);

public record PlaceAlertPreferenceDto(string Kind, Guid PlaceId, string Mode);

public record SetPlaceAlertPreferenceRequest(string Mode);

public record ReplyAlertUnreadCountDto(int Count);

public record ReplyAlertPreferenceDto(Guid RootPostId, string Mode);

public record SetReplyAlertPreferenceRequest(string Mode);

public record RegisterPushDeviceRequest(string Platform, string Token);

public record RemovePushDeviceRequest(string Token);

public record ReplyAlertPush(
    Guid AlertId,
    Guid? RootPostId,
    string ServerDid,
    string Title,
    string Body,
    int ReplyCount,
    string Kind = "thread",
    string? Slug = null,
    string? ParentSlug = null,
    string? ForumParentSlug = null);
