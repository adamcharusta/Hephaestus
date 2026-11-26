using Hephaestus.Core.Localization;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Trust;

namespace Hephaestus.Host.Api;

/// <summary>
/// The endpoints of the Trusted Users (Story 1.8), on the control socket. A user is stored by GitHub's numeric id, found through the
/// gateway; the login in an address is only what the Owner typed (AD-15).
/// </summary>
internal static class TrustedUserApi
{
    public static void Map(IEndpointRouteBuilder routes)
    {
        var users = routes.MapGroup("/api/trusted-users");
        users.MapGet(string.Empty, ListAsync);
        users.MapPut("/{login}", AddAsync);
        users.MapDelete("/{login}", RemoveAsync);
    }

    private static async Task<IResult> ListAsync(IReadStore store, CancellationToken cancellationToken)
    {
        var users = await store.ListTrustedUsersAsync(cancellationToken);
        return Results.Json(new TrustedUserListDocument([.. users.Select(Documents.TrustedUser)]), ApiJson.Default.TrustedUserListDocument);
    }

    private static async Task<IResult> AddAsync(string login, TrustedUserService service, CancellationToken cancellationToken)
    {
        var result = await service.AddAsync(login, cancellationToken);
        return result.User is { } user
            ? Results.Json(Documents.TrustedUser(user), ApiJson.Default.TrustedUserDocument, statusCode: result.Changed ? StatusCodes.Status201Created : StatusCodes.Status200OK)
            : Problem(result.Failure, login);
    }

    private static async Task<IResult> RemoveAsync(string login, TrustedUserService service, CancellationToken cancellationToken)
    {
        var result = await service.RemoveAsync(login, cancellationToken);
        return result.User is { } user ? Results.Json(Documents.TrustedUser(user), ApiJson.Default.TrustedUserDocument) : Problem(result.Failure, login);
    }

    private static ProblemResult Problem(TrustedUserFailure failure, string login) => failure switch
    {
        TrustedUserFailure.InvalidLogin => new(StatusCodes.Status400BadRequest, "trusted-user-login-invalid", MessageIds.ProblemTrustedUserLoginInvalidTitle, new Message(MessageIds.ProblemTrustedUserLoginInvalidDetail)),
        TrustedUserFailure.UserNotFound => new(StatusCodes.Status404NotFound, "github-user-not-found", MessageIds.ProblemTrustedUserGithubNotFoundTitle, new Message(MessageIds.ProblemTrustedUserGithubNotFoundDetail, login)),
        TrustedUserFailure.OwnIdentity => new(StatusCodes.Status422UnprocessableEntity, "trusted-user-own-identity", MessageIds.ProblemTrustedUserOwnIdentityTitle, new Message(MessageIds.ProblemTrustedUserOwnIdentityDetail, login)),
        TrustedUserFailure.NotTrusted => new(StatusCodes.Status404NotFound, "trusted-user-not-found", MessageIds.ProblemTrustedUserNotFoundTitle, new Message(MessageIds.ProblemTrustedUserNotFoundDetail, login)),
        TrustedUserFailure.NotConnected => new(StatusCodes.Status404NotFound, "github-not-connected", MessageIds.ProblemGitHubNotConnectedTitle, new Message(MessageIds.ProblemGitHubNotConnectedDetail)),
        TrustedUserFailure.KeyUnreadable => new(StatusCodes.Status400BadRequest, "github-key-unreadable", MessageIds.ProblemGitHubCredentialRejectedTitle, new Message(MessageIds.ProblemGitHubKeyUnreadableDetail)),
        TrustedUserFailure.Rejected => new(StatusCodes.Status422UnprocessableEntity, "github-credential-rejected", MessageIds.ProblemGitHubCredentialRejectedTitle, new Message(MessageIds.ProblemGitHubCredentialRejectedDetail)),
        _ => new(StatusCodes.Status502BadGateway, "github-unreachable", MessageIds.ProblemGitHubUnreachableTitle, new Message(MessageIds.ProblemGitHubUnreachableDetail)),
    };
}
