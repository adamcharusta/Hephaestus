using Hephaestus.Core.Validation;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Workspaces;

public sealed partial class DockerWorkspaceRuntime
{
    public async Task<StartedStep> StartValidationAsync(ValidationCommandRequest request, string idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var container = request.Container();

        // What is wrong with the command's container is found before the tree is touched.
        _ = StepContainer(container, idempotencyKey);

        // A crash between the checkout and the container leaves a tree no command has run on, and a checkout makes it again. A container that exists is one a command runs in or ran in: its tree is not made again beneath it, and the
        // bundle, which is read once, is discarded unread.
        if (await FindStepContainerAsync(request.StepRunId, cancellationToken) is null)
        {
            await CheckoutAsync(request.Checkout(), idempotencyKey, cancellationToken);
        }
        else
        {
            var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.BundlesDirectory));
            DeleteBundleFile(directory, Path.GetFullPath(Path.Combine(directory, request.Tip.Path.Replace('/', Path.DirectorySeparatorChar))));
        }

        return await StartStepAsync(container, idempotencyKey, cancellationToken);
    }
}
