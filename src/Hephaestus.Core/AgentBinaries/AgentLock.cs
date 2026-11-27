using System.Globalization;
using System.Text.RegularExpressions;
using Hephaestus.Core.Aliases;
using Hephaestus.Core.Localization;

namespace Hephaestus.Core.AgentBinaries;

/// <summary>The processor architectures the Agent CLIs are pinned for; the host runs on one of them.</summary>
public enum AgentArchitecture
{
    X64,
    Arm64,
}

/// <summary>The C library an executable was built for; <see cref="Static"/> runs on either (Codex CLI).</summary>
public enum AgentLibc
{
    Glibc,
    Musl,
    Static,
}

/// <summary>
/// One executable of an Agent CLI as <c>deploy/agents.lock</c> pins it (AD-8): its own digest, which is checked at every start, and
/// where the vendor publishes it. When the vendor ships an archive, <see cref="ArchiveSha256"/> is the digest of that archive and
/// <see cref="Sha256"/> the digest of the one executable taken out of it.
/// </summary>
public sealed record AgentBinary(Agent Agent, string Version, AgentArchitecture Architecture, AgentLibc Libc, string File, string Sha256, Uri Url, string? ArchiveSha256)
{
    /// <summary>The executable's place in the agent binaries volume, with forward slashes: <c>claude-code/x64-musl/claude</c>.</summary>
    public string RelativePath => $"{Agent.Tag()}/{Architecture.Tag()}-{Libc.Tag()}/{File}";

    public bool IsArchive => ArchiveSha256 is not null;
}

/// <summary>A line of the lock that cannot be accepted, or a pin it lacks. The message is for the person who changes the lock.</summary>
public sealed class AgentLockException(Message info) : Exception(info.Id)
{
    public Message Info { get; } = info;
}

public static class AgentLockTags
{
    public static string Tag(this AgentArchitecture architecture) => architecture switch
    {
        AgentArchitecture.X64 => "x64",
        AgentArchitecture.Arm64 => "arm64",
        _ => throw new ArgumentOutOfRangeException(nameof(architecture), architecture, null),
    };

    public static string Tag(this AgentLibc libc) => libc switch
    {
        AgentLibc.Glibc => "glibc",
        AgentLibc.Musl => "musl",
        AgentLibc.Static => "static",
        _ => throw new ArgumentOutOfRangeException(nameof(libc), libc, null),
    };
}

/// <summary>
/// The pinned Agent CLIs of a release (AD-8, Story 3.1): one version of each Agent and the SHA-256 of every executable, per architecture
/// and, where the vendor builds per C library, per C library. Parsing is a pure function of the text and demands the whole matrix, so a
/// lock that omits an executable is refused before anything is downloaded.
/// </summary>
public sealed partial class AgentLock
{
    private const int ExecutableColumns = 7;
    private const int ArchiveColumns = 8;

    /// <summary>What each Agent consists of: the builds it has and the executables of each. Nothing else is accepted.</summary>
    private static readonly IReadOnlyDictionary<Agent, (AgentLibc[] Libcs, string[] Files)> Matrix = new Dictionary<Agent, (AgentLibc[], string[])>
    {
        [Agent.ClaudeCode] = ([AgentLibc.Glibc, AgentLibc.Musl], ["claude"]),
        [Agent.CodexCli] = ([AgentLibc.Static], ["codex", "codex-code-mode-host"]),
    };

    private AgentLock(IReadOnlyList<AgentBinary> binaries) => Binaries = binaries;

    public IReadOnlyList<AgentBinary> Binaries { get; }

    /// <summary>The version pinned for an Agent.</summary>
    public string VersionOf(Agent agent) => Binaries.First(binary => binary.Agent == agent).Version;

    public IReadOnlyList<AgentBinary> For(AgentArchitecture architecture) => [.. Binaries.Where(binary => binary.Architecture == architecture)];

    public static AgentLock Parse(string text)
    {
        var binaries = new List<AgentBinary>();
        var versions = new Dictionary<Agent, string>();
        var number = 0;
        foreach (var raw in text.Split('\n'))
        {
            number++;
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var binary = ParseLine(line, number);
            if (versions.TryGetValue(binary.Agent, out var pinned) && pinned != binary.Version)
            {
                throw Invalid(MessageIds.AgentsLockVersion, number, binary.Agent.Tag(), pinned, binary.Version);
            }

            versions[binary.Agent] = binary.Version;
            if (binaries.Any(other => other.RelativePath == binary.RelativePath))
            {
                throw Invalid(MessageIds.AgentsLockDuplicate, number, binary.RelativePath);
            }

            binaries.Add(binary);
        }

        foreach (var (agent, (libcs, files)) in Matrix)
        {
            foreach (var architecture in Enum.GetValues<AgentArchitecture>())
            {
                foreach (var (libc, file) in libcs.SelectMany(libc => files.Select(file => (libc, file))))
                {
                    if (!binaries.Any(b => b.Agent == agent && b.Architecture == architecture && b.Libc == libc && b.File == file))
                    {
                        throw new AgentLockException(new Message(MessageIds.AgentsLockMissing, $"{agent.Tag()}/{architecture.Tag()}-{libc.Tag()}/{file}"));
                    }
                }
            }
        }

        return new AgentLock(binaries);
    }

    private static AgentBinary ParseLine(string line, int number)
    {
        var columns = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (columns.Length is not (ExecutableColumns or ArchiveColumns))
        {
            throw Invalid(MessageIds.AgentsLockColumns, number, columns.Length.ToString(CultureInfo.InvariantCulture));
        }

        if (!Agents.TryParse(columns[0], out var agent) || columns[0] != agent.Tag())
        {
            throw Invalid(MessageIds.AgentsLockValue, number, "agent", columns[0]);
        }

        var version = VersionPattern().IsMatch(columns[1]) ? columns[1] : throw Invalid(MessageIds.AgentsLockValue, number, "version", columns[1]);
        var architecture = Enum.GetValues<AgentArchitecture>().Cast<AgentArchitecture?>().FirstOrDefault(a => a!.Value.Tag() == columns[2])
            ?? throw Invalid(MessageIds.AgentsLockValue, number, "arch", columns[2]);
        var libc = Enum.GetValues<AgentLibc>().Cast<AgentLibc?>().FirstOrDefault(l => l!.Value.Tag() == columns[3])
            ?? throw Invalid(MessageIds.AgentsLockValue, number, "libc", columns[3]);
        var (libcs, files) = Matrix[agent];
        if (!libcs.Contains(libc) || !files.Contains(columns[4]))
        {
            throw Invalid(MessageIds.AgentsLockNotAnExecutable, number, columns[4], $"{agent.Tag()} ({libc.Tag()})");
        }

        var sha256 = DigestPattern().IsMatch(columns[5]) ? columns[5] : throw Invalid(MessageIds.AgentsLockValue, number, "sha256", columns[5]);
        if (!Uri.TryCreate(columns[6], UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps)
        {
            throw Invalid(MessageIds.AgentsLockValue, number, "url", columns[6]);
        }

        string? archive = null;
        if (columns.Length == ArchiveColumns)
        {
            archive = DigestPattern().IsMatch(columns[7]) ? columns[7] : throw Invalid(MessageIds.AgentsLockValue, number, "archive-sha256", columns[7]);
        }

        return new AgentBinary(agent, version, architecture, libc, columns[4], sha256, url, archive);
    }

    private static AgentLockException Invalid(string id, int line, params string[] arguments) =>
        new(new Message(id, [line.ToString(CultureInfo.InvariantCulture), .. arguments]));

    [GeneratedRegex("^[0-9]+(\\.[0-9]+){2}$")]
    private static partial Regex VersionPattern();

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex DigestPattern();
}
