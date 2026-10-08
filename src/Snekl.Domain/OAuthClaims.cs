namespace Snekl.Domain;

public static class OAuthClaims
{
    public const string Azp = "azp";
    public const string Bot = "bot";
    public const string InstallId = "install_id";
    public const string TokenUse = "token_use";
    public const string ContentBaseUrl = "content_base_url";
    /// <summary>Unix seconds when the user last authenticated with a password (OIDC auth_time).</summary>
    public const string AuthTime = "auth_time";
    /// <summary>SHA-256 JWK thumbprint of the browser key that must sign each request.</summary>
    public const string CnfJkt = "cnf_jkt";

    public const string TokenUseUser = "user";
    public const string TokenUseInstall = "install";
    public const string TokenUseBot = "bot";
}

public static class ExtensionResourceTypes
{
    public const string Server = "server";
    public const string Forum = "forum";
}
