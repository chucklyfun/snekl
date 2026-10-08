namespace Snekl.Domain.Identity;

public record PersonClusterRefDto(
    Guid Id,
    string Did,
    string CanonicalUsername,
    string DisplayName,
    string PrimaryAuthServerUrl,
    string? DisambiguationTag,
    string DisplayLabel);

public record MapIdentityRequest(
    Guid PersonClusterId,
    string AuthorDid,
    string CanonicalUsername,
    string DisplayName,
    string PrimaryAuthServerUrl,
    string? DisambiguationTag,
    string IssuerUrl,
    Guid SubjectId);

public record RegisterTrustedIssuerRequest(string IssuerUrl, string JwksUrl, string Name);
