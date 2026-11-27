using Hephaestus.Core.Ports;
using Hephaestus.Core.Projects;
using Hephaestus.Core.Trust;
using Octokit;
using Octokit.Internal;

namespace Hephaestus.GitHub;

/// <summary>
/// The gateway's reads of a repository: which repository the App is installed on and what one of its branches is (Story 1.10), and a file of a
/// branch (Story 1.11).
/// </summary>
public sealed partial class GitHubGateway
{
    public async Task<GitHubRepositoryResult> FindRepositoryAsync(string fullName, string? branch, CancellationToken cancellationToken)
    {
        if (!ProjectName.TryParse(fullName, out var owner, out _))
        {
            return GitHubRepositoryResult.Failed(GitHubRepositoryFailure.NotInstalled);
        }

        var (appId, privateKey) = await ReadCredentialAsync(cancellationToken);
        if (appId is null || privateKey is null)
        {
            return GitHubRepositoryResult.Failed(GitHubRepositoryFailure.NotConnected);
        }

        var jwt = GitHubAppJwt.TryCreate(appId.Value, privateKey, _time.GetUtcNow());
        if (jwt is null)
        {
            return GitHubRepositoryResult.Failed(GitHubRepositoryFailure.KeyUnreadable);
        }

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(_deadline);
        using var transport = new HttpClientAdapter(_handler);
        transport.SetRequestTimeout(RequestTimeout);
        try
        {
            var app = CreateClient(new Credentials(jwt, AuthenticationType.Bearer), transport);
            var installations = await app.GitHubApps.GetAllInstallationsForCurrent(new ApiOptions { PageSize = PageSize }).WaitAsync(limit.Token);

            // Only an installation on the owner's account can reach the repository, and a suspended one reaches nothing.
            foreach (var installation in installations.Where(i => i.SuspendedAt is null && GitHubLogin.SameLogin(i.Account?.Login ?? string.Empty, owner)).OrderBy(i => i.Id))
            {
                var token = await app.GitHubApps.CreateInstallationToken(installation.Id).WaitAsync(limit.Token);
                var client = CreateClient(new Credentials(token.Token), transport);
                var reached = await client.GitHubApps.Installation.GetAllRepositoriesForCurrent(new ApiOptions { PageSize = PageSize }).WaitAsync(limit.Token);
                var repository = reached.Repositories.FirstOrDefault(r => ProjectName.SameName(r.FullName, fullName));
                if (repository is null)
                {
                    continue;
                }

                return branch is null
                    ? GitHubRepositoryResult.Found(new GitHubRepository(repository.Id, repository.FullName, Branch: null))
                    : await ReadBranchAsync(client, repository, branch, limit.Token);
            }

            return GitHubRepositoryResult.Failed(GitHubRepositoryFailure.NotInstalled);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested && IsOutage(exception))
        {
            return GitHubRepositoryResult.Failed(GitHubRepositoryFailure.Unreachable);
        }
        catch (ApiException)
        {
            return GitHubRepositoryResult.Failed(GitHubRepositoryFailure.Rejected);
        }
    }

    private static async Task<GitHubRepositoryResult> ReadBranchAsync(GitHubClient client, Repository repository, string branch, CancellationToken cancellationToken)
    {
        try
        {
            var found = await client.Repository.Branch.Get(repository.Id, branch).WaitAsync(cancellationToken);
            return GitHubRepositoryResult.Found(new GitHubRepository(repository.Id, repository.FullName, new GitHubBranch(found.Name, found.Protected)));
        }
        catch (NotFoundException)
        {
            return GitHubRepositoryResult.Failed(GitHubRepositoryFailure.BranchNotFound);
        }
    }

    public async Task<GitHubFileResult> ReadFileAsync(long repositoryId, string branch, string path, int maximumLength, CancellationToken cancellationToken)
    {
        var (appId, privateKey) = await ReadCredentialAsync(cancellationToken);
        if (appId is null || privateKey is null)
        {
            return GitHubFileResult.Failed(GitHubFileFailure.NotConnected);
        }

        var jwt = GitHubAppJwt.TryCreate(appId.Value, privateKey, _time.GetUtcNow());
        if (jwt is null)
        {
            return GitHubFileResult.Failed(GitHubFileFailure.KeyUnreadable);
        }

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(_deadline);
        using var transport = new HttpClientAdapter(_handler);
        transport.SetRequestTimeout(RequestTimeout);
        try
        {
            var app = CreateClient(new Credentials(jwt, AuthenticationType.Bearer), transport);
            var installations = await app.GitHubApps.GetAllInstallationsForCurrent(new ApiOptions { PageSize = PageSize }).WaitAsync(limit.Token);

            // The repository is found by its id, not its name, which a rename or a transfer changes; only an installation that holds it answers.
            foreach (var installation in installations.Where(i => i.SuspendedAt is null).OrderBy(i => i.Id))
            {
                var token = await app.GitHubApps.CreateInstallationToken(installation.Id).WaitAsync(limit.Token);
                var client = CreateClient(new Credentials(token.Token), transport);
                if (!await HoldsRepositoryAsync(client, repositoryId, limit.Token))
                {
                    continue;
                }

                return await ReadBranchFileAsync(client, repositoryId, branch, path, maximumLength, limit.Token);
            }

            return GitHubFileResult.Failed(GitHubFileFailure.NotInstalled);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested && IsOutage(exception))
        {
            return GitHubFileResult.Failed(GitHubFileFailure.Unreachable);
        }
        catch (ApiException)
        {
            return GitHubFileResult.Failed(GitHubFileFailure.Rejected);
        }
    }

    /// <summary>
    /// Whether the installation the client holds the token of has the repository among its own. A read of the repository by its id would
    /// answer for any public repository, installed or not, so the installation's own list is asked.
    /// </summary>
    private static async Task<bool> HoldsRepositoryAsync(GitHubClient client, long repositoryId, CancellationToken cancellationToken)
    {
        var reached = await client.GitHubApps.Installation.GetAllRepositoriesForCurrent(new ApiOptions { PageSize = PageSize }).WaitAsync(cancellationToken);
        return reached.Repositories.Any(repository => repository.Id == repositoryId);
    }

    /// <summary>
    /// The file as it is at the tip of the branch: the tip is read first and the file is then asked for at that commit's id, never at the branch's name, so that
    /// the bytes and the commit id are one thing even when the branch moves between the two calls. Another branch is never read.
    /// </summary>
    private static async Task<GitHubFileResult> ReadBranchFileAsync(GitHubClient client, long repositoryId, string branch, string path, int maximumLength, CancellationToken cancellationToken)
    {
        string commitId;
        try
        {
            commitId = (await client.Repository.Branch.Get(repositoryId, branch).WaitAsync(cancellationToken)).Commit.Sha;
        }
        catch (NotFoundException)
        {
            return GitHubFileResult.Failed(GitHubFileFailure.BranchNotFound);
        }

        IReadOnlyList<RepositoryContent> contents;
        try
        {
            contents = await client.Repository.Content.GetAllContentsByRef(repositoryId, path, commitId).WaitAsync(cancellationToken);
        }
        catch (NotFoundException)
        {
            return GitHubFileResult.Failed(GitHubFileFailure.NotFound, commitId);
        }

        // A directory answers with its entries, and a link or a submodule is not the file's own bytes.
        if (contents is not [{ Type.Value: ContentType.File } file])
        {
            return GitHubFileResult.Failed(GitHubFileFailure.NotFound, commitId);
        }

        // GitHub sends no content for a large file, so the size is judged before anything is decoded.
        if (file.Size > maximumLength)
        {
            return GitHubFileResult.Failed(GitHubFileFailure.TooLarge, commitId);
        }

        try
        {
            return GitHubFileResult.Read(new GitHubFile(Convert.FromBase64String(file.EncodedContent ?? string.Empty), commitId));
        }
        catch (FormatException)
        {
            return GitHubFileResult.Failed(GitHubFileFailure.Unreachable, commitId);
        }
    }
}
