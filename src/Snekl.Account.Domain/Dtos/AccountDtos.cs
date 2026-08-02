namespace Snekl.Account.Domain.Dtos;

public record RegisterRequest(
    string Username,
    string Email,
    string Password,
    string DisplayName,
    string? Did = null,
    string? KeyOwnershipProof = null);

public record ImportIdentityRequest(
    string Username,
    string Email,
    string Password,
    string DisplayName,
    string IdentityCredentialJson);

public record LoginRequest(string Username, string Password);

public record DevResetPasswordRequest(string Username, string NewPassword);

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

public record UpdateProfileRequest(string DisplayName, string Bio, string? DisambiguationTag);

public record PartnerTrustDto(string PartnerBaseUrl, bool IsMutual, DateTime CreatedAt);

public record JwksResponse(IReadOnlyList<JwkKey> Keys);
public record JwkKey(string Kty, string Use, string Kid, string Alg, string N, string E);
