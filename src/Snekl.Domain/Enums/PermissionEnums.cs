namespace Snekl.Domain.Enums;

[Flags]
public enum ServerPermissions : ulong
{
    None = 0,
    ViewServer = 1UL << 0,
    CreateForum = 1UL << 1,
    ManageServerMembers = 1UL << 2,
    ManageServerRoles = 1UL << 3,
    ManageServerSettings = 1UL << 4,
    ServerAdministrator = 1UL << 5,
    RequestFederation = 1UL << 6,
    AcceptFederation = 1UL << 7,
    /// <summary>Legacy wire alias for CreateForum with GrantMode.Request. Prefer CreateForum + Request mode.</summary>
    RequestCreateForum = 1UL << 8,
    ManageContentBlocks = 1UL << 9,
    CreateWatercooler = 1UL << 10,
    /// <summary>Read server change history (roles, members, settings).</summary>
    ViewHistory = 1UL << 11,
    /// <summary>Use emotes from other forums on this node (plazas and server context).</summary>
    UseOtherForumEmotes = 1UL << 12,
    /// <summary>Use emotes from other content servers.</summary>
    UseOtherServerEmotes = 1UL << 13
}

public static class ServerPermissionSets
{
    public const ServerPermissions AdministratorImplied =
        ServerPermissions.ViewServer |
        ServerPermissions.CreateForum |
        ServerPermissions.ManageServerMembers |
        ServerPermissions.ManageServerRoles |
        ServerPermissions.ManageServerSettings |
        ServerPermissions.RequestFederation |
        ServerPermissions.AcceptFederation |
        ServerPermissions.ManageContentBlocks |
        ServerPermissions.CreateWatercooler |
        ServerPermissions.ViewHistory |
        ServerPermissions.UseOtherForumEmotes |
        ServerPermissions.UseOtherServerEmotes;
}

[Flags]
public enum ForumPermissions : ulong
{
    None = 0,
    ViewForum = 1UL << 0,
    CreateThread = 1UL << 1,
    Reply = 1UL << 2,
    EditOwnPost = 1UL << 3,
    DeleteOwnPost = 1UL << 4,
    ManagePosts = 1UL << 5,
    ManageMembers = 1UL << 6,
    ManageRoles = 1UL << 7,
    ManageForum = 1UL << 8,
    ForumAdministrator = 1UL << 9,
    /// <summary>Know the forum exists (listing). Separate from <see cref="ViewForum"/> content access.</summary>
    SeeForum = 1UL << 10,
    SeeContentBlocks = 1UL << 11,
    ManageContentBlocks = 1UL << 12,
    /// <summary>Load unveiled NSFW media in this forum. Clipped until the viewer passes the age check.</summary>
    ViewNsfwMedia = 1UL << 13,
    /// <summary>Add or remove the NSFW veil on someone else's attachments.</summary>
    MarkNsfwMedia = 1UL << 14,
    /// <summary>
    /// Request a follow-back so the pair can open a DM. Leave off @everyone / new-member roles
    /// so forums can require an established role before people solicit conversations.
    /// </summary>
    RequestDirectMessage = 1UL << 15,
    /// <summary>Read forum change history and post edit history (authors still see their own edits).</summary>
    ViewHistory = 1UL << 16,
    /// <summary>Use emotes from other forums on this content server.</summary>
    UseOtherForumEmotes = 1UL << 17,
    /// <summary>Use emotes from other content servers.</summary>
    UseOtherServerEmotes = 1UL << 18
}

public static class ForumPermissionSets
{
    public const ForumPermissions AdministratorImplied =
        ForumPermissions.ViewForum |
        ForumPermissions.CreateThread |
        ForumPermissions.Reply |
        ForumPermissions.EditOwnPost |
        ForumPermissions.DeleteOwnPost |
        ForumPermissions.ManagePosts |
        ForumPermissions.ManageMembers |
        ForumPermissions.ManageRoles |
        ForumPermissions.ManageForum |
        ForumPermissions.SeeForum |
        ForumPermissions.SeeContentBlocks |
        ForumPermissions.ManageContentBlocks |
        ForumPermissions.ViewNsfwMedia |
        ForumPermissions.MarkNsfwMedia |
        ForumPermissions.RequestDirectMessage |
        ForumPermissions.ViewHistory |
        ForumPermissions.UseOtherForumEmotes |
        ForumPermissions.UseOtherServerEmotes;
}

[Flags]
public enum WatercoolerPermissions : ulong
{
    None = 0,
    SeeWatercooler = 1UL << 0,
    ViewWatercooler = 1UL << 1,
    SpeakWatercooler = 1UL << 2,
    ManageWatercooler = 1UL << 3,
    WatercoolerAdministrator = 1UL << 4,
    /// <summary>Read watercooler change history and post edit history (authors still see their own edits).</summary>
    ViewHistory = 1UL << 5
}

public static class WatercoolerPermissionSets
{
    public const WatercoolerPermissions AdministratorImplied =
        WatercoolerPermissions.SeeWatercooler |
        WatercoolerPermissions.ViewWatercooler |
        WatercoolerPermissions.SpeakWatercooler |
        WatercoolerPermissions.ManageWatercooler |
        WatercoolerPermissions.ViewHistory;
}

public static class PermissionNames
{
    public static IReadOnlyList<string> ToNames(ServerPermissions perms) =>
        PermissionModeMap.ToWireNames(PermissionModeMap.FromServerFlags(perms), server: true);

    public static IReadOnlyList<string> ToNames(ForumPermissions perms) =>
        PermissionModeMap.ToWireNames(PermissionModeMap.FromForumFlags(perms), server: false);

    public static IReadOnlyList<string> ToNames(WatercoolerPermissions perms) =>
        PermissionModeMap.ToWireNames(PermissionModeMap.FromWatercoolerFlags(perms), server: false);

    public static IReadOnlyList<string> ToNames(IReadOnlyDictionary<string, GrantMode> modes, bool server) =>
        PermissionModeMap.ToWireNames(modes, server);
}
