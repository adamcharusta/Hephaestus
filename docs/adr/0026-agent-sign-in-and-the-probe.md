# ADR-0026: Choose each Agent's sign-in from what its adapter supports, resolve the credential only when a container starts, and probe it in a fixed image with no Task volume

- **Status:** Accepted
- **Date:** 2026-11-27
- **Spine:** AD-10, AD-16, AD-20

## Context

An adapter names the secret an Agent needs in each authentication mode (Story 3.8), and the runtime starts containers (Story 3.2), but nothing connected them: the Owner could not say how an Agent signs in, could not supply its credential, and could not learn that it does not work before a Task meets the failure. The architecture says that Hephaestus implements no sign-in, that a secret is a file only the `hephaestus` container reads, that an invocation carries a reference and only the runtime resolves it, and that a probe is run by the runtime with no Task volume and its finding is a fact about the Agent, not a failed step run.

## Decision

**The mode is the Owner's, the support is the adapter's.** `AgentSignInService` (Core) accepts a mode only when `IAgentAdapter.SupportsAuthentication` says yes, and refuses with the adapter's own message otherwise. An Agent with no adapter in the release is refused with a message that says so, which is the case of Codex CLI until Story 3.9. The credential is the secret named by the reference of the adapter's probe for that mode, so that the name is the adapter's and not a second list in Core.
The mode is Installation Configuration (`AgentSettings`), written by the API handler in its own transaction like every other setting.

**The credential is one value on standard input.** `agent set-auth <agent> --mode <mode>` reads it exactly as `secret set` does and the API takes it as the body of a PUT. An empty body keeps the secret that is stored, so that choosing a mode again needs no retyping, and is refused when none is. The service stores nothing unless everything is acceptable, and it never reads a secret value back.

**A probe is a container the runtime runs and no one journals.** `IWorkspaceRuntime.RunProbeAsync` takes the invocation `DescribeProbe` described. The probe is neither a step run nor an effect on GitHub: it is a question the Owner asks, with an answer that a retry would only repeat, so it appends no Signal, writes no journal line, and the loop does not know it. It is made of the same parts as a step container, with the Task, the context, and the Methodology left out: the Agent mounts, a throwaway `HOME`, its own internal network, and its own egress proxy that allows exactly the hosts of the invocation, whatever the installation's egress default. Its output is read from Docker's log once it ends, masked, and cut to its last 256 KiB, where the result is; it is not stored.
The runtime removes the container, the proxy, and the network when it ends, and removes the container by force when it outlives its limit (only the step watcher stops a step's container, AD-18; a probe has none). A probe that a crash of the host left is not found again by anything yet: it carries the labels of the installation and the role `agent-probe`, which is what the recovery of Story 3.17 selects on.

**The probe image is a stock image, fixed by digest.** `docker.io/library/debian:13-slim@sha256:...` (`ProbeImage.Reference`) meets the Project image contract and nothing more, so the probe shows that the Agent CLIs run in an image that has only what a Project image must have. It belongs to no Project, so a check needs no Project; it is a constant of the release, with its digest, so that every installation probes in the same image, and the Engine test resolves it as glibc with no declared volume and runs a probe in it.

**A finding is a capacity record, not a failure.** What a probe finds is stored on the capacity record of the Agent credential (`AgentCapacities`, keyed by Agent and mode) with the time of the probe: `ready`, or `unavailable` with a message id and its parameters. Never a line the CLI printed, because that is text a vendor or an Agent wrote. `AgentCapacity.HasCapacity` is false for an unavailable Agent, and for one that a usage limit holds until its time (Story 7.1 writes it), so an unavailable Agent has no capacity and no step run is touched (AD-20).
A probe that could not run, because Docker or the image or the proxy failed, is not a finding: the runtime reports a fault of Docker as `WorkspaceRuntimeException`, nothing is recorded, and the Owner is told why. Storing a credential forgets the finding of that mode's credential, which may be another; keeping the one that is stored keeps it, and so keeps the time until which a usage limit holds the Agent. A probe that ends after the mode was chosen again records nothing, and the host runs one probe at a time.

**The credential reaches a container as an environment variable and in nothing else.** `StepContainerRequest.SecretReferences` carries the references of the invocation; the runtime reads exactly those through `ISecretStore` after it has found that no container of the step run exists and before it creates a network, a proxy, or a volume, adds the values to the container's environment, and keeps them nowhere else. A secret that is not stored refuses the start. Labels, names, volumes, the proxy's environment, and the journal never hold a value, and a test looks at everything the double Engine was asked to create. A reference to a file, to `HOME`, or to a variable the runtime sets itself is refused before anything is made, because no adapter needs one and a file would have to be placed in the container's file system.
The container's configuration is readable by whoever holds the Docker socket, which is the `hephaestus` container alone (AD-7).

## Alternatives considered

- *Run the probe in the Project's image.* Rejected: a probe would need a Project, and an Agent would be unavailable because of an image.
- *Publish a `hephaestus-probe` image.* Rejected: a second image to build, publish, and keep in step with the release, for something Debian already is.
- *Make the probe a step run with a step kind.* Rejected: it has no Task and no Workspace, and AD-20 says an Agent found unavailable by a probe is not a failed step run.
- *Store what the CLI printed with the finding.* Rejected: it is untrusted text, and the finding needs the reason, not the transcript.
- *Write the credential into the container as a file.* Not done: nothing needs it, and Codex CLI's subscription credential, which would, is not supported.
- *Run a probe at every start of the host, or before every Task.* Not done: a probe asks the vendor a question, which costs tokens or plan usage, so it runs when the Owner asks. Dispatch and Task start will consult the record (Stories 3.14, 3.15).
