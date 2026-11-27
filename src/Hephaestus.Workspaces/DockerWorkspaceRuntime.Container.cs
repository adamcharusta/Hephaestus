using Docker.DotNet.Models;
using Hephaestus.Core.AgentBinaries;
using Hephaestus.Core.Aliases;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Workspaces;
using Hephaestus.Workspaces.Egress;
using Message = Hephaestus.Core.Localization.Message;
using SecretReference = Hephaestus.Core.Invocations.SecretReference;

namespace Hephaestus.Workspaces;

public sealed partial class DockerWorkspaceRuntime
{
    private const long MinimumMemoryBytes = 6L * 1024 * 1024;

    /// <summary>The temporary <c>HOME</c>: memory only, discarded with the container, and owned by the step user (AD-7).</summary>
    private static readonly string HomeOptions = $"rw,exec,nosuid,nodev,size=512m,uid={StepContainerLayout.UserId},gid={StepContainerLayout.GroupId},mode=0700";

    /// <summary>
    /// The definition of a step container (AD-7): user <c>10001:10001</c>, every capability dropped, <c>no-new-privileges</c>, Docker's default
    /// seccomp profile (nothing is named, so the default applies), a process limit, no published port, the limits of the Task, exactly the mounts of the
    /// container layout, and no network but the internal one of its step run, whose only other member is the egress proxy. The image's entry point and working directory are replaced, so that an image cannot change what runs.
    /// </summary>
    private CreateContainerParameters StepContainer(StepContainerRequest request, string idempotencyKey, bool probe = false)
    {
        if (request.Command.Count == 0 || string.IsNullOrEmpty(request.Command[0]))
        {
            throw new ArgumentException("A step needs a command.", nameof(request));
        }

        // Docker reads a limit of zero as no limit, and refuses a memory limit below a few mebibytes.
        if (request.Limits.CpuMillis <= 0 || request.Limits.MemoryBytes < MinimumMemoryBytes)
        {
            throw new ArgumentException("A step needs a processor limit above zero and a memory limit of at least 6 MiB.", nameof(request));
        }

        // The proxy variables and git's rewrite are the runtime's, and replace any of the same name an invocation or an image sets; HOME likewise (AD-7).
        ValidateHosts(request.AllowedEgressHosts);
        ValidateSecretReferences(request.SecretReferences);
        var environment = request.Environment
            .Where(pair => pair.Key is not "HOME" && !EgressEnvironment.Variables.ContainsKey(pair.Key))
            .Select(pair => EnvironmentEntry(pair.Key, pair.Value))
            .Concat(EgressEnvironment.Variables.Select(pair => EnvironmentEntry(pair.Key, pair.Value)))
            .Append($"HOME={StepContainerLayout.Home}")
            .ToList();
        return new CreateContainerParameters
        {
            Name = probe ? WorkspaceLabels.AgentProbe(request.StepRunId) : WorkspaceLabels.StepContainer(request.StepRunId),
            Image = request.Image.Reference,
            User = StepContainerLayout.User,
            Entrypoint = [request.Command[0]],
            Cmd = [.. request.Command.Skip(1)],
            Env = environment,
            WorkingDir = StepContainerLayout.Workspace,
            Labels = WorkspaceLabels.For(options.InstallationId, TaskLabel(request), probe ? null : request.StepRunId, idempotencyKey, probe ? WorkspaceLabels.AgentProbeRole : WorkspaceLabels.StepRole),
            HostConfig = new HostConfig
            {
                CapDrop = ["ALL"],
                SecurityOpt = ["no-new-privileges"],
                Privileged = false,
                PublishAllPorts = false,
                PidsLimit = StepContainerLayout.ProcessLimit,
                Memory = request.Limits.MemoryBytes,
                MemorySwap = request.Limits.MemoryBytes,
                NanoCPUs = request.Limits.CpuMillis * 1_000_000L,
                NetworkMode = WorkspaceLabels.StepNetwork(request.StepRunId),
                RestartPolicy = new RestartPolicy { Name = RestartPolicyKind.No },
                Mounts = probe ? [.. AgentMounts(request.Image)] : StepMounts(request),
                Tmpfs = new Dictionary<string, string> { [StepContainerLayout.Home] = HomeOptions },
            },
        };
    }

    /// <summary>The Task a container's labels name: none for an Agent probe, which has none (<see cref="Guid.Empty"/>).</summary>
    private static Guid? TaskLabel(StepContainerRequest request) => request.TaskId == Guid.Empty ? null : request.TaskId;

    /// <summary>
    /// A secret reaches a container as an environment variable and in no other way: a file would have to be placed in the container's file system, which no invocation needs yet. Another target, or a variable the
    /// runtime sets itself, is a mistake of whoever made the request, found before anything is created.
    /// </summary>
    private static void ValidateSecretReferences(IReadOnlyList<SecretReference>? references)
    {
        foreach (var reference in references ?? [])
        {
            if (!reference.IsEnvironmentVariable || reference.Target == "HOME" || EgressEnvironment.Variables.ContainsKey(reference.Target))
            {
                throw new ArgumentException($"The secret {reference.Name} cannot be placed at '{reference.Target}': only an environment variable that the runtime does not set itself can hold a secret.", nameof(references));
            }
        }

        if ((references ?? []).GroupBy(reference => reference.Target, StringComparer.Ordinal).Any(group => group.Count() > 1))
        {
            throw new ArgumentException("Two secrets have the same target.", nameof(references));
        }
    }

    /// <summary>The values of the references, read from the secret store now: each is read when this runs and held only until the container is created. A secret that is not stored refuses the start.</summary>
    private async Task<List<(string Name, string Value)>> ResolveSecretsAsync(IReadOnlyList<SecretReference>? references, CancellationToken cancellationToken)
    {
        var resolved = new List<(string Name, string Value)>();
        foreach (var reference in references ?? [])
        {
            var value = await secrets.ReadAsync(reference.Name, cancellationToken)
                ?? throw new WorkspaceRuntimeException(new Message(MessageIds.WorkspaceSecretMissing, reference.Name));
            resolved.Add((reference.Target, value));
        }

        return resolved;
    }

    /// <summary>Puts the resolved values in the environment of the definition, replacing a variable of the same name that the invocation set.</summary>
    private static void AddSecrets(CreateContainerParameters definition, List<(string Name, string Value)> resolved)
    {
        foreach (var (name, value) in resolved)
        {
            var environment = definition.Env!;
            for (var index = environment.Count - 1; index >= 0; index--)
            {
                if (environment[index].StartsWith(name + "=", StringComparison.Ordinal))
                {
                    environment.RemoveAt(index);
                }
            }

            environment.Add(EnvironmentEntry(name, value));
        }
    }

    /// <summary>A host the proxy is to allow is a host name: the proxy's list is one line of names, and a name that cannot be one could change what the line says.</summary>
    private static void ValidateHosts(IReadOnlyList<string>? hosts)
    {
        foreach (var host in hosts ?? [])
        {
            if (host.Length is 0 or > EgressRefusals.MaximumHostLength || EgressAllowlist.Normalize(host) is not { Length: > 0 } normalized || !normalized.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_'))
            {
                throw new ArgumentException($"'{host}' is not a host name.", nameof(hosts));
            }
        }
    }

    private static string EnvironmentEntry(string name, string value) =>
        name.Length == 0 || name.Contains('=', StringComparison.Ordinal) || name.Contains('\0', StringComparison.Ordinal) || value.Contains('\0', StringComparison.Ordinal)
            ? throw new ArgumentException($"'{name}' is not a name an environment can hold.", nameof(name))
            : $"{name}={value}";

    /// <summary>
    /// Exactly these, and no other: the Task volume, the context and the Methodology, the build of each Agent CLI that matches the image's C
    /// library together with the CA bundle, and nothing of the host. Every named volume is mounted with <c>volume-nocopy</c>, so that what an image holds at a mount point
    /// can neither enter the volume nor change the owner of its root (AD-6).
    /// </summary>
    private List<Mount> StepMounts(StepContainerRequest request)
    {
        var mounts = new List<Mount>
        {
            VolumeMount(WorkspaceLabels.TaskVolume(request.TaskId), StepContainerLayout.Workspace, readOnly: false),
            VolumeMount(WorkspaceLabels.ContextVolume(request.StepRunId), StepContainerLayout.Context, readOnly: true),
            VolumeMount(WorkspaceLabels.MethodologyVolume(request.StepRunId), StepContainerLayout.Methodology, readOnly: true),
        };
        mounts.AddRange(AgentMounts(request.Image));
        return mounts;
    }

    /// <summary>
    /// The agent binaries volume holds every build; a step container sees one directory for each Agent, whatever the C library of its image, so that an
    /// invocation's paths do not depend on it (AD-8). Docker mounts a sub-directory of a volume for each, and the certificate directory beside them.
    /// </summary>
    private IEnumerable<Mount> AgentMounts(ResolvedImage image)
    {
        var libc = image.Libc == CLibrary.Musl ? AgentLibc.Musl : AgentLibc.Glibc;
        foreach (var agent in options.AgentLock.For(image.Architecture).GroupBy(binary => binary.Agent))
        {
            var build = agent.Where(binary => binary.Libc == libc || binary.Libc == AgentLibc.Static).Select(binary => binary.RelativePath[..binary.RelativePath.LastIndexOf('/')]).Distinct().Single();
            yield return AgentsVolumeMount(build, $"{StepContainerLayout.Agents}/{agent.Key.Tag()}");
        }

        yield return AgentsVolumeMount("certs", $"{StepContainerLayout.Agents}/certs");
    }

    private Mount AgentsVolumeMount(string subpath, string target) => new()
    {
        Type = "volume",
        Source = options.AgentsVolume,
        Target = target,
        ReadOnly = true,
        VolumeOptions = new VolumeOptions { NoCopy = true, Subpath = subpath },
    };

    private static Mount VolumeMount(string volume, string target, bool readOnly) => new()
    {
        Type = "volume",
        Source = volume,
        Target = target,
        ReadOnly = readOnly,
        VolumeOptions = new VolumeOptions { NoCopy = true },
    };

    /// <summary>
    /// A container that is created and never started, for a job Docker does with a container's mounts or files: no network, no capability, and only the mounts it is for. Its
    /// entry point is named, because Docker refuses to create a container from an image that has none, such as one built from scratch, and the C library of such an image must be
    /// reported, not a refusal of Docker's.
    /// </summary>
    private static CreateContainerParameters InertContainer(string name, string image, Dictionary<string, string> labels, List<Mount> mounts) => new()
    {
        Name = name,
        Image = image,
        Labels = labels,
        Entrypoint = ["/hephaestus-never-started"],
        HostConfig = new HostConfig
        {
            CapDrop = ["ALL"],
            SecurityOpt = ["no-new-privileges"],
            NetworkMode = "none",
            Mounts = mounts,
        },
    };

    /// <summary>Removing a helper takes the anonymous volumes an image declares with it, which Docker makes when it creates a container, and which no label selects.</summary>
    private static readonly ContainerRemoveParameters HelperRemoval = new() { Force = true, RemoveVolumes = true };

    /// <summary>The files of a directory are written by one archive, so two with one path, or a file where another is a directory, cannot be.</summary>
    private static void ValidateFiles(IReadOnlyList<WorkspaceFile> files)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            if (!paths.Add(file.Path))
            {
                throw new ArgumentException($"'{file.Path}' is in the list twice.", nameof(files));
            }
        }

        foreach (var path in paths)
        {
            var names = path.Split('/');
            for (var depth = 1; depth < names.Length; depth++)
            {
                if (paths.Contains(string.Join('/', names.Take(depth))))
                {
                    throw new ArgumentException($"'{string.Join('/', names.Take(depth))}' is a file and a directory.", nameof(files));
                }
            }
        }
    }
}
