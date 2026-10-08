namespace Snekl.Account.Infrastructure.Security;

public static class AuthAdminAuth
{
    public const string Scheme = "AuthAdmin";
    public const string Policy = "AuthOperator";
    public const string Audience = "snekl-auth-admin";
    public const string RoleClaim = "role";
    public const string Role = "auth_operator";
}
