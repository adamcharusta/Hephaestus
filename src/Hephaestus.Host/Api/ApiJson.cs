using System.Text.Json.Serialization;

namespace Hephaestus.Host.Api;

/// <summary>The version document, the same shape on the API and in <c>--json</c> output.</summary>
internal sealed record VersionDocument(string Version);

/// <summary>A secret, by name; the value is never part of any document.</summary>
internal sealed record SecretNameDocument(string Name);

/// <summary>The stored secrets, by name only.</summary>
internal sealed record SecretListDocument(IReadOnlyList<SecretNameDocument> Secrets);

/// <summary>One place the GitHub App is installed, with the repositories it reaches there.</summary>
internal sealed record GitHubInstallationDocument(long Id, string Account, string AccountType, string RepositorySelection, bool Suspended, IReadOnlyList<string> Repositories);

/// <summary>One required permission: what the App needs, what it has (<c>none</c>, <c>read</c>, <c>write</c>, <c>admin</c>), and whether that is enough.</summary>
internal sealed record GitHubPermissionDocument(string Name, string Required, string Granted, bool Present);

/// <summary>A permission the App must not have, and the level at which it has it.</summary>
internal sealed record GitHubWarningDocument(string Name, string Granted);

/// <summary>Hephaestus's own GitHub App as GitHub reports it. It holds no credential.</summary>
internal sealed record GitHubIdentityDocument(
    long AppId,
    string Slug,
    string Name,
    string Owner,
    bool Ready,
    IReadOnlyList<GitHubInstallationDocument> Installations,
    IReadOnlyList<GitHubPermissionDocument> Permissions,
    IReadOnlyList<GitHubWarningDocument> Warnings);

/// <summary>
/// A Trusted User (AD-15). <c>userId</c> is GitHub's numeric id and the identity; <c>login</c> is display only. <c>addedAt</c> is UTC
/// ISO 8601.
/// </summary>
internal sealed record TrustedUserDocument(long UserId, string Login, string AddedAt);

/// <summary>The Trusted Users, ascending by login.</summary>
internal sealed record TrustedUserListDocument(IReadOnlyList<TrustedUserDocument> TrustedUsers);

/// <summary>An RFC 9457 problem details document. <c>type</c> is never translated; <c>title</c> and <c>detail</c> are.</summary>
internal sealed record ProblemDocument(string Type, string Title, int Status, string Detail);

/// <summary>Source-generated JSON: camelCase keys, as every Hephaestus document.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(VersionDocument))]
[JsonSerializable(typeof(SecretNameDocument))]
[JsonSerializable(typeof(SecretListDocument))]
[JsonSerializable(typeof(GitHubIdentityDocument))]
[JsonSerializable(typeof(TrustedUserDocument))]
[JsonSerializable(typeof(TrustedUserListDocument))]
[JsonSerializable(typeof(ProblemDocument))]
internal sealed partial class ApiJson : JsonSerializerContext;
