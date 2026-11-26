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

/// <summary>An Alias. Either <c>agent</c> and <c>model</c> are set, or <c>effort</c> is; the others are null. <c>addedAt</c> is UTC ISO 8601.</summary>
internal sealed record AliasDocument(string Name, string? Agent, string? Model, string? Effort, string AddedAt);

/// <summary>The Aliases, ascending by name.</summary>
internal sealed record AliasListDocument(IReadOnlyList<AliasDocument> Aliases);

/// <summary>What an Alias is to resolve to: an Agent with a Model, or an Effort.</summary>
internal sealed record AliasRequest(string? Agent, string? Model, string? Effort);

/// <summary>The installation limits in their written form; a limit nobody set is null, never zero.</summary>
internal sealed record LimitsDocument(string? TaskDuration, string? Cpus, string? Memory);

/// <summary>The limits to change, in their written form: a null leaves that limit as it is.</summary>
internal sealed record LimitsRequest(string? TaskDuration, string? Cpus, string? Memory);

/// <summary>A protected path.</summary>
internal sealed record ProtectedPathDocument(string Pattern);

/// <summary>The protected paths, ascending.</summary>
internal sealed record ProtectedPathListDocument(IReadOnlyList<string> ProtectedPaths);

/// <summary>An installation setting: its value now, its default, and whether the Owner has set it (<c>isSet</c> is false while the value is the default).</summary>
internal sealed record SettingDocument(string Name, string Value, string Default, bool IsSet);

/// <summary>The settings that have a default.</summary>
internal sealed record SettingListDocument(IReadOnlyList<SettingDocument> Settings);

/// <summary>The Installation Configuration as one document. Secrets are by name only; no value is ever read to make it.</summary>
internal sealed record ConfigurationDocument(
    IReadOnlyList<TrustedUserDocument> TrustedUsers,
    IReadOnlyList<AliasDocument> Aliases,
    LimitsDocument Limits,
    IReadOnlyList<string> ProtectedPaths,
    IReadOnlyList<SettingDocument> Settings,
    IReadOnlyList<SecretNameDocument> Secrets);

/// <summary>
/// A Project (FR-1). <c>repositoryId</c> is GitHub's numeric id and the identity; <c>name</c> (<c>owner/repo</c>) is display only.
/// <c>removedAt</c> is null while Hephaestus works on the Project. <c>change</c> (<c>registered</c>, <c>updated</c>, <c>unchanged</c>) and
/// <c>baseBranchProtected</c> are set only in the answer to a registration; <c>baseBranchProtected</c> is what GitHub reports.
/// </summary>
internal sealed record ProjectDocument(long RepositoryId, string Name, string BaseBranch, string RegisteredAt, string? RemovedAt, string? Change, bool? BaseBranchProtected);

/// <summary>The Projects, ascending by name.</summary>
internal sealed record ProjectListDocument(IReadOnlyList<ProjectDocument> Projects);

/// <summary>The Base Branch a Project is to be registered with.</summary>
internal sealed record ProjectRequest(string? BaseBranch);

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
[JsonSerializable(typeof(AliasDocument))]
[JsonSerializable(typeof(AliasListDocument))]
[JsonSerializable(typeof(AliasRequest))]
[JsonSerializable(typeof(LimitsDocument))]
[JsonSerializable(typeof(LimitsRequest))]
[JsonSerializable(typeof(ProtectedPathDocument))]
[JsonSerializable(typeof(ProtectedPathListDocument))]
[JsonSerializable(typeof(SettingDocument))]
[JsonSerializable(typeof(SettingListDocument))]
[JsonSerializable(typeof(ConfigurationDocument))]
[JsonSerializable(typeof(ProjectDocument))]
[JsonSerializable(typeof(ProjectListDocument))]
[JsonSerializable(typeof(ProjectRequest))]
[JsonSerializable(typeof(ProblemDocument))]
internal sealed partial class ApiJson : JsonSerializerContext;
