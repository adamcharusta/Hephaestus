using Hephaestus.Core.GitHub;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Ports;

namespace Hephaestus.Host.Api;

/// <summary>
/// The endpoints of Hephaestus's own GitHub identity (Story 1.7), on the control socket. The private key travels in the body of the
/// PUT and never in an address, and no document the API answers contains it; the gateway alone reads it back (AD-4).
/// </summary>
internal static class GitHubApi
{
    public static void Map(IEndpointRouteBuilder routes)
    {
        routes.MapPut("/api/github/app/{appId}", ConnectAsync);
        routes.MapGet("/api/github/identity", CheckAsync);
    }

    private static async Task<IResult> ConnectAsync(string appId, HttpRequest request, ICodeHost gateway, CancellationToken cancellationToken)
    {
        if (!long.TryParse(appId, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id) || id <= 0)
        {
            return new ProblemResult(StatusCodes.Status400BadRequest, "github-app-id-invalid", MessageIds.ProblemGitHubAppIdInvalidTitle, new Message(MessageIds.ProblemGitHubAppIdInvalidDetail));
        }

        var privateKey = await ControlApi.ReadValueAsync(request.Body, cancellationToken);
        if (ControlApi.RefuseValue(privateKey) is { } refusal)
        {
            return refusal;
        }

        return Answer(await gateway.ConnectIdentityAsync(id, privateKey, cancellationToken));
    }

    private static async Task<IResult> CheckAsync(ICodeHost gateway, CancellationToken cancellationToken) =>
        Answer(await gateway.CheckIdentityAsync(cancellationToken));

    private static IResult Answer(GitHubIdentityResult result) => result.Failure switch
    {
        GitHubIdentityFailure.None => Results.Json(Document(result.Identity!), ApiJson.Default.GitHubIdentityDocument),
        GitHubIdentityFailure.NotConnected => new ProblemResult(StatusCodes.Status404NotFound, "github-not-connected", MessageIds.ProblemGitHubNotConnectedTitle, new Message(MessageIds.ProblemGitHubNotConnectedDetail)),
        GitHubIdentityFailure.KeyUnreadable => new ProblemResult(StatusCodes.Status400BadRequest, "github-key-unreadable", MessageIds.ProblemGitHubCredentialRejectedTitle, new Message(MessageIds.ProblemGitHubKeyUnreadableDetail)),
        GitHubIdentityFailure.Rejected => new ProblemResult(StatusCodes.Status422UnprocessableEntity, "github-credential-rejected", MessageIds.ProblemGitHubCredentialRejectedTitle, new Message(MessageIds.ProblemGitHubCredentialRejectedDetail)),
        _ => new ProblemResult(StatusCodes.Status502BadGateway, "github-unreachable", MessageIds.ProblemGitHubUnreachableTitle, new Message(MessageIds.ProblemGitHubUnreachableDetail)),
    };

    private static GitHubIdentityDocument Document(GitHubIdentity identity) => new(
        identity.AppId,
        identity.Slug,
        identity.Name,
        identity.Owner,
        identity.Ready,
        [.. identity.Installations.Select(i => new GitHubInstallationDocument(i.Id, i.Account, i.AccountType, i.RepositorySelection, i.Suspended, i.Repositories))],
        [.. identity.Permissions.Select(p => new GitHubPermissionDocument(p.Name, LevelName(p.Required), LevelName(p.Granted), p.Present))],
        [.. identity.Warnings.Select(w => new GitHubWarningDocument(w.Name, LevelName(w.Granted)))]);

    /// <summary>The level as GitHub names it, and as the JSON documents of Hephaestus write an enum: lower case.</summary>
    private static string LevelName(GitHubPermissionLevel level) => level switch
    {
        GitHubPermissionLevel.Read => "read",
        GitHubPermissionLevel.Write => "write",
        GitHubPermissionLevel.Admin => "admin",
        _ => "none",
    };
}
