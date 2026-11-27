using Hephaestus.Core.AgentBinaries;
using Hephaestus.Core.Localization;

namespace Hephaestus.Core.Workspaces;

/// <summary>The C library of a Project image (AD-8): the one that decides which build of an Agent CLI is mounted into its containers.</summary>
public enum CLibrary
{
    Glibc,
    Musl,
}

/// <summary>
/// An image as the runtime resolved it when a Task started (AD-8): the reference that names exactly one image, the digest, the processor
/// architecture, and the C library. The loop records it in the configuration snapshot, and every step container of the Task is created from
/// <see cref="Reference"/>, so a tag that moves later changes nothing for a Task that has started.
/// </summary>
/// <param name="Reference">What every container of the Task is created from: <c>name@sha256:...</c>, or the image id of an image no registry knows.</param>
/// <param name="Digest">The digest, <c>sha256:</c> and 64 hexadecimal digits.</param>
public sealed record ResolvedImage(string Reference, string Digest, AgentArchitecture Architecture, CLibrary Libc);

/// <summary>
/// An image that a Task cannot run in (AD-8): it could not be fetched, it is not built for this machine, or its C library cannot be
/// established. The Task does not start, and the message tells the Owner why.
/// </summary>
public sealed class ImageNotUsableException(Message info, Exception? innerException = null) : Exception(info.Id, innerException)
{
    public Message Info { get; } = info;
}

/// <summary>
/// The runtime cannot give a step the isolation it promises (AD-7): a workspace network exists and is not an internal network, or the egress proxy did not start. Nothing is started, and the message says what to change.
/// </summary>
public sealed class WorkspaceRuntimeException(Message info, Exception? innerException = null) : Exception(info.Id, innerException)
{
    public Message Info { get; } = info;
}
