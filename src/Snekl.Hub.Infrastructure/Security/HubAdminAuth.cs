namespace Snekl.Hub.Infrastructure.Security;

public static class HubAdminAuth
{
    public const string Scheme = "HubAdmin";
    public const string Policy = "HubOperator";
    public const string Audience = "snekl-hub-admin";
    public const string RoleClaim = "role";
    public const string Role = "hub_operator";
    public const string MemberAuthorizationHeader = "X-Snekl-Member-Authorization";
}
