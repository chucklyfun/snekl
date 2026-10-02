namespace Snekl.Account.Domain.Dtos;

public record DevicePublicJwk(string Kty, string Crv, string X, string Y);

public record RegisterRequest(
    string Username,
    string Email,
    string Password,
    string DisplayName,
    string? Did = null,
    string? KeyOwnershipProof = null,
    DevicePublicJwk? DeviceKey = null);

public record ImportIdentityRequest(
    string Username,
    string Email,
    string Password,
    string DisplayName,
    string IdentityCredentialJson,
    DevicePublicJwk? DeviceKey = null);

public record LoginRequest(string Email, string Password, DevicePublicJwk? DeviceKey = null);

public record ReauthenticateRequest(string Password, DevicePublicJwk? DeviceKey = null);

public record ForgotPasswordRequest(string Email);

public record ResetPasswordRequest(string Token, string NewPassword);

public record VerifyEmailRequest(string Token);

public record MessageResponse(string Message);

public record ForgotPasswordResponse(string Message, string? ResetUrl = null);

public record DevResetPasswordRequest(string Email, string NewPassword);

public record AuthIdentityDto(
    Guid Id,
    string AuthServerUrl,
    bool IsPrimary,
    bool HasLocalPassword);

public record PersonClusterDto(
    Guid Id,
    string Did,
    string CanonicalUsername,
    string DisplayName,
    string Email,
    bool EmailVerified,
    string Bio,
    string PrimaryAuthServerUrl,
    string? DisambiguationTag,
    string DisplayLabel,
    IReadOnlyList<AuthIdentityDto> Identities);

public record RingSessionDto(
    Guid RingSessionId,
    string AccessToken,
    DateTime ExpiresAt,
    PersonClusterDto Person,
    IReadOnlyList<PartnerSessionDto> PartnerSessions,
    string? GeneratedKeyPrivatePkcs8Base64 = null);

public record PartnerSessionDto(string AuthServerUrl, string AccessToken, DateTime ExpiresAt);

public record EstablishPartnerTrustRequest(string PartnerBaseUrl);

public record IncomingPartnerTrustRequest(string PartnerBaseUrl, string PartnerName, string PartnerSigningKey);

public record ProvisionPartnerRequest(string PartnerBaseUrl, string? Password);

public record RingSessionRequest(
    Guid PersonClusterId,
    Guid SourceIdentityId,
    string SourceAuthServerUrl,
    string CanonicalUsername,
    string DisplayName,
    string Email,
    string Signature,
    DateTime ExpiresAt);

public record VouchPayload(
    Guid PersonClusterId,
    string PersonDid,
    Guid SourceIdentityId,
    string SourceAuthServerUrl,
    string CanonicalUsername,
    string DisplayName,
    string Email,
    string Bio,
    string? DisambiguationTag,
    DateTime ExpiresAt,
    string Signature);

public record ExportIdentityResponse(string IdentityCredentialJson, DateTime ExpiresAt);

public record UpdateProfileRequest(
    string Username,
    string Email,
    string DisplayName,
    string Bio,
    string? DisambiguationTag);

public record UpdateProfileResponse(PersonClusterDto Person, string AccessToken);

public record PartnerTrustDto(string PartnerBaseUrl, bool IsMutual, DateTime CreatedAt);

public record JwksResponse(IReadOnlyList<JwkKey> Keys);
public record JwkKey(string Kty, string Use, string Kid, string Alg, string N, string E);

public record ContentServerMembershipDto(
    string ServerDid,
    string BaseUrl,
    string Name,
    Guid? NodeId,
    string Status,
    DateTime SubscribedAt);

public record SubscribeContentServerRequest(string BaseUrl);

public record UpdateContentServerStatusRequest(string Status);

public record ContentServerStatusPresetInfo(
    string Preset,
    string Label,
    string Description,
    bool IncludedInFeed,
    bool SortFirst);

public record FederationMembershipDto(
    string BaseUrl,
    string Name,
    string? AuthIssuer,
    string Status,
    DateTime SubscribedAt);

public record SubscribeFederationRequest(string BaseUrl);

public record UpdateFederationStatusRequest(string Status);

public record ForumFavoriteDto(
    string ServerDid,
    string ForumId,
    string Name,
    string Slug,
    string? ParentSlug,
    string ServerName,
    DateTime AddedAt);

public record UpsertForumFavoriteRequest(
    string ServerDid,
    string ForumId,
    string Name,
    string Slug,
    string? ParentSlug,
    string ServerName);

public record FollowDto(
    string Did,
    string? DisplayName,
    string? Username,
    DateTime AddedAt);

public record UpsertFollowRequest(
    string Did,
    string? DisplayName = null,
    string? Username = null);

public record FollowContainsDto(bool Follows);

public record ForumNavFolderDto(string Id, string Name, string? ParentId, int Position);

public record ForumNavFolderItemDto(
    string FolderId,
    string ServerDid,
    string ForumId,
    string Name,
    string Slug,
    string? ParentSlug,
    string ServerName,
    int Position);

public record ForumNavHiddenDto(string SectionId, string ServerDid, string ForumId);

public record ForumNavPeerDto(
    string Id,
    bool Enabled,
    int Position,
    IReadOnlyList<string>? GroupAxes = null);

public record ForumNavLayoutDto(
    IReadOnlyList<ForumNavFolderDto> Folders,
    IReadOnlyList<ForumNavFolderItemDto> Items,
    IReadOnlyList<ForumNavHiddenDto>? Hidden = null,
    IReadOnlyList<string>? GroupAxes = null,
    IReadOnlyList<ForumNavPeerDto>? Peers = null);

public record AuthServerInfoDto(string Name, string BaseUrl, string Did, string JwksUrl);

public record AdminBootstrapDto(
    string Kind,
    string Name,
    string PublicBaseUrl,
    string? Did,
    string AuthLoginUrl);

public static class AuthOperatorStates
{
    public const string Unclaimed = "unclaimed";
    public const string SetupRequired = "setupRequired";
    public const string Ready = "ready";
    public const string Forbidden = "forbidden";
}

public record AuthOperatorStatusDto(string State);
public record AuthOperatorLoginRequest(string? AdminPassword = null, string? NewAdminPassword = null);
public record AuthOperatorDto(Guid Id, string Did, string Username, bool HasPassword, DateTime CreatedAt);
public record AuthAdminAuthResponse(string Token, AuthOperatorDto Operator);
public record InviteAuthOperatorRequest(string Username);

public record RegisterOAuthAppRequest(
    string Name,
    IReadOnlyList<string> RedirectUris,
    string? AdminUrl = null,
    string? IframeUrl = null,
    string? ClientId = null);

public record OAuthAppDto(
    string ClientId,
    string Name,
    IReadOnlyList<string> RedirectUris,
    string? AdminUrl,
    string? IframeUrl);

public record RegisterOAuthAppResponse(
    string ClientId,
    string ClientSecret,
    string Name,
    IReadOnlyList<string> RedirectUris,
    string? AdminUrl,
    string? IframeUrl);

public record AuthorizeInstallRequest(
    string ClientId,
    string RedirectUri,
    string CodeChallenge,
    string CodeChallengeMethod,
    string ContentBaseUrl,
    Guid InstallId,
    string? State = null);

public record AuthorizeInstallResponse(string RedirectTo);

public record OAuthTokenRequest(
    string GrantType,
    string? Code = null,
    string? RedirectUri = null,
    string? ClientId = null,
    string? ClientSecret = null,
    string? CodeVerifier = null,
    string? RefreshToken = null);

public record OAuthTokenResponse(
    string AccessToken,
    string TokenType,
    int ExpiresIn,
    string RefreshToken,
    string TokenUse,
    Guid InstallId,
    string ContentBaseUrl,
    Snekl.Domain.Dtos.RateLimitPolicyDto? RateLimiting = null);

public record CreateOAuthBotRequest(string Username, string DisplayName);

public record ReissueOAuthBotRequest(string Username);

public record OAuthBotDto(
    Guid PersonClusterId,
    string Did,
    string Username,
    string DisplayName);

public record OAuthBotCreatedResponse(
    OAuthBotDto Bot,
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAt);
