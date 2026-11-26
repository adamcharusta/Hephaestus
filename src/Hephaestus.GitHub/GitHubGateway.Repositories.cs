using Hephaestus.Core.Ports;
using Hephaestus.Core.Projects;
using Hephaestus.Core.Trust;
using Octokit;
using Octokit.Internal;

namespace Hephaestus.GitHub;

/// <summary>The gateway's reads of a repository (Story 1.10): which repository the App is installed on, and what one of its branches is.</summary>
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
}
