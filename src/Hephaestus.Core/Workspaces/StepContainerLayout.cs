namespace Hephaestus.Core.Workspaces;

/// <summary>
/// What a step container looks like from the inside (AD-7, AR-34): the paths an adapter's invocation and a Methodology template name, and the
/// user every step runs as. They are part of the versioned public surface of an installation, so they are written once, here.
/// </summary>
public static class StepContainerLayout
{
    /// <summary>The repository; the Task volume is mounted here.</summary>
    public const string Workspace = "/workspace";

    /// <summary>The context files of the step run, read-only (AD-12).</summary>
    public const string Context = "/hephaestus/context";

    /// <summary>The Methodology's files, read-only (AD-14).</summary>
    public const string Methodology = "/hephaestus/methodology";

    /// <summary>The Agent CLIs, read-only. Under it each Agent has a directory of its own, <c>claude-code</c> and <c>codex-cli</c>, that holds the build that matches the image's C library.</summary>
    public const string Agents = "/hephaestus/agents";

    /// <summary>The CA certificate bundle of the <c>hephaestus</c> image, for a CLI that needs one.</summary>
    public const string CaBundle = Agents + "/certs/ca-certificates.crt";

    /// <summary>The throwaway <c>HOME</c>, a tmpfs that is discarded with the container.</summary>
    public const string Home = "/hephaestus/home";

    public const int UserId = 10001;
    public const int GroupId = 10001;

    /// <summary>The user and group of every step container, the same in every image.</summary>
    public const string User = "10001:10001";

    /// <summary>The most processes a step container may have.</summary>
    public const int ProcessLimit = 512;

    /// <summary>The prefix of the name of every container, volume, and network Hephaestus creates.</summary>
    public const string NamePrefix = "hephaestus-";
}
