using Hephaestus.Core.Localization;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Projects;

namespace Hephaestus.Host.Api;

/// <summary>
/// The endpoints of the Projects (Story 1.10), on the control socket. A Project is named <c>owner/repo</c> in the <c>name</c> query value and given
/// its Base Branch in a JSON body; it is stored by GitHub's repository id (FR-1).
/// </summary>
internal static class ProjectApi
{
    public static void Map(IEndpointRouteBuilder routes)
    {
        var projects = routes.MapGroup("/api/projects");
        projects.MapGet(string.Empty, ListAsync);

        // Reading a file from GitHub changes nothing here, so it is a read; it is on the control socket with everything else until the Dashboard has its own listener.
        projects.MapGet("/check", CheckAsync);

        // The name is a query value, so that whatever the Owner typed reaches Core as typed: in a path, "." and ".." segments would be
        // resolved by the client into another address, and a name that is not owner/repo would be an unknown address, not a refusal.
        projects.MapPut(string.Empty, AddAsync);
        projects.MapDelete(string.Empty, RemoveAsync);
    }

    private static async Task<IResult> ListAsync(IReadStore store, CancellationToken cancellationToken, bool all = false)
    {
        var projects = await store.ListProjectsAsync(includeRemoved: all, cancellationToken);
        return Results.Json(new ProjectListDocument([.. projects.Select(project => Documents.Project(project))]), ApiJson.Default.ProjectListDocument);
    }

    private static async Task<IResult> AddAsync(string? name, HttpRequest request, ProjectService service, CancellationToken cancellationToken)
    {
        name ??= string.Empty;
        if (await ApiBody.ReadJsonAsync(request, ApiJson.Default.ProjectRequest, cancellationToken) is not { } body)
        {
            return ApiBody.Invalid();
        }

        var result = await service.AddAsync(name, body.BaseBranch, cancellationToken);
        return result.Project is { } project
            ? Results.Json(Documents.Project(project, result.Change, result.BaseBranchProtected), ApiJson.Default.ProjectDocument, statusCode: result.Change == ProjectChange.Registered ? StatusCodes.Status201Created : StatusCodes.Status200OK)
            : Problem(result.Failure, name, body.BaseBranch);
    }

    private static async Task<IResult> RemoveAsync(string? name, ProjectService service, CancellationToken cancellationToken)
    {
        name ??= string.Empty;
        var result = await service.RemoveAsync(name, cancellationToken);
        return result.Project is { } project ? Results.Json(Documents.Project(project), ApiJson.Default.ProjectDocument) : Problem(result.Failure, name, null);
    }

    private static async Task<IResult> CheckAsync(string? name, ProjectConfigurationService service, CancellationToken cancellationToken)
    {
        name ??= string.Empty;
        var check = await service.CheckAsync(name, cancellationToken);
        return check is { Failure: ProjectFailure.None, Project: { } project, Status: { } status, CommitId: { } commitId }
            ? Results.Json(Documents.ConfigurationCheck(project, status, commitId, check.Issues, check.Configuration), ApiJson.Default.ConfigurationCheckDocument)
            : Problem(check.Failure, name, check.Project?.BaseBranch);
    }

    private static ProblemResult Problem(ProjectFailure failure, string name, string? branch) => failure switch
    {
        ProjectFailure.InvalidName => new(StatusCodes.Status400BadRequest, "project-name-invalid", MessageIds.ProblemProjectNameInvalidTitle, new Message(MessageIds.ProblemProjectNameInvalidDetail)),
        ProjectFailure.InvalidBranch => new(StatusCodes.Status400BadRequest, "project-branch-invalid", MessageIds.ProblemProjectBranchInvalidTitle, new Message(MessageIds.ProblemProjectBranchInvalidDetail)),
        ProjectFailure.NotInstalled => new(StatusCodes.Status404NotFound, "project-not-installed", MessageIds.ProblemProjectNotInstalledTitle, new Message(MessageIds.ProblemProjectNotInstalledDetail, name)),
        ProjectFailure.BranchNotFound => new(StatusCodes.Status404NotFound, "project-branch-not-found", MessageIds.ProblemProjectBranchNotFoundTitle, new Message(MessageIds.ProblemProjectBranchNotFoundDetail, name, branch ?? string.Empty)),
        ProjectFailure.NotRegistered => new(StatusCodes.Status404NotFound, "project-not-registered", MessageIds.ProblemProjectNotRegisteredTitle, new Message(MessageIds.ProblemProjectNotRegisteredDetail, name)),
        ProjectFailure.NotConnected => new(StatusCodes.Status404NotFound, "github-not-connected", MessageIds.ProblemGitHubNotConnectedTitle, new Message(MessageIds.ProblemGitHubNotConnectedDetail)),
        ProjectFailure.KeyUnreadable => new(StatusCodes.Status400BadRequest, "github-key-unreadable", MessageIds.ProblemGitHubCredentialRejectedTitle, new Message(MessageIds.ProblemGitHubKeyUnreadableDetail)),
        ProjectFailure.Rejected => new(StatusCodes.Status422UnprocessableEntity, "github-credential-rejected", MessageIds.ProblemGitHubCredentialRejectedTitle, new Message(MessageIds.ProblemGitHubCredentialRejectedDetail)),
        _ => new(StatusCodes.Status502BadGateway, "github-unreachable", MessageIds.ProblemGitHubUnreachableTitle, new Message(MessageIds.ProblemGitHubUnreachableDetail)),
    };
}
