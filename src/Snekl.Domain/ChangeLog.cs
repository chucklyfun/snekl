namespace Snekl.Domain;

public static class ChangeLogScopes
{
    public const string Server = "server";
    public const string Forum = "forum";
    public const string Watercooler = "watercooler";
}

public static class ChangeLogActions
{
    public const string Created = "created";
    public const string Updated = "updated";
    public const string Deleted = "deleted";
    public const string Assigned = "assigned";
}

public static class ChangeLogEntityTypes
{
    public const string ServerRole = "ServerRole";
    public const string ServerMember = "ServerMember";
    public const string Forum = "Forum";
    public const string ForumRole = "ForumRole";
    public const string ForumMember = "ForumMember";
    public const string ForumBan = "ForumBan";
    public const string PostReport = "PostReport";
    public const string Watercooler = "Watercooler";
    public const string WatercoolerRole = "WatercoolerRole";
    public const string NodeConfig = "NodeConfig";
}
