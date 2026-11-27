namespace Hephaestus.Core.Readiness;

/// <summary>
/// The image a probe runs in (Story 3.10, AD-20): it belongs to no Project, so that checking an Agent needs no Project and no Task. It is stock Debian, which meets the Project image
/// contract (Linux, glibc, <c>bash</c> present, no declared volume) and nothing more, and it is named by the digest of its multi-platform index so that every installation of
/// a release probes in the same image. A change of it is a change of this constant, of <c>docs/agent-clis.md</c>, and of the release.
/// </summary>
public static class ProbeImage
{
    public const string Reference = "docker.io/library/debian:13-slim@sha256:8aa5a7bf5a1955bddbe7d74fcdd0b91ab30e74362a8b73af672b0481b35b44a0";
}
