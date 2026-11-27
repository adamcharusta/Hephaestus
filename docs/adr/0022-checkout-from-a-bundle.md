# ADR-0022: The checkout of every step: a bare mirror, one bundle, a tools container

- **Status:** Accepted
- **Date:** 2026-11-27
- **Spine:** AD-6, AD-9, AD-4

## Context

ADR-0009 decided that only the committed tree survives a step: before every step a credential-free container re-creates `/workspace` from the last pushed commit. ADR-0010 decided that code crosses the boundary of a
Workspace only as a git bundle, and that the orchestrator runs git only where no Agent can write. Story 3.5 makes both true: something has to keep the repository the bundle is made from, make the bundle,
hand it to a container, and empty the volume before the clone.

## Decision

**One bare mirror per Project, kept by the gateway.** `GitHubMirrors` in `Hephaestus.GitHub` keeps `<repository id>.git` on the `hephaestus-mirrors` volume, which only the `hephaestus` container mounts. It is made with an empty template, so it has no
hooks directory, no sample hook, and no working tree. `git fetch` brings every branch (`+refs/heads/*:refs/heads/*`, pruned). The mirror is a cache: it holds no credential and can be removed and fetched again.

**One place a process is started.** `GitProcess` is the only code that starts a process. It runs git with `--git-dir` naming a mirror (a path outside the mirrors directory is refused before anything starts), with the mirrors
directory as its working directory, with `core.hooksPath` pointing at `/dev/null`, no system or user configuration, no prompt, only the protocols it is allowed (`https`), and an environment of its own. The orchestrator therefore
runs no git command in any directory an Agent can write to, and a test (`GitMirrorRulesTests`) fails when any other file starts a process.

**The token is an environment variable of one process.** The installation token that reads the repository is given to the fetching process as `GIT_CONFIG_COUNT`, `GIT_CONFIG_KEY_0=http.https://github.com/.extraHeader`, and `GIT_CONFIG_VALUE_0`, scoped to GitHub's address so that a redirect elsewhere does not carry it.
It is never an argument, a file, a remote URL, or an entry of the mirror's configuration, and what git says is masked before it is returned.

**One bundle per checkout.** `CreateBundleAsync` gives the commit (the Task's last pushed tip, or its start commit on the Base Branch) a temporary name in the mirror, writes `git bundle create` to a temporary file under the bundles directory of the data volume, moves it into place, removes the name, and returns a
`BundleReference`: the path, the SHA-256, and the tip. The file is named after the step run, so a retry replaces it, and it has a bound (2 GiB).

**The checkout runs in the tools image, with nothing.** `DockerWorkspaceRuntime.CheckoutAsync` checks the file against the reference (a regular file, not a link, under the bundles directory, within the bound, with the checksum), creates a container of the `hephaestus-tools` image
as `10001:10001` with no network, every capability dropped, and the Task volume as its only mount (`volume-nocopy`), places the bundle in its own file system as one archive entry, starts it, waits for it, removes it, and deletes the bundle. The environment holds the branch and the tip and nothing else.
The script (`CheckoutScript`) empties `/workspace` (dot files and unreadable directories included), makes a repository in it from an empty template, verifies the bundle, checks that it has exactly one head and that the head is the tip the runtime named, fetches it
into the Task branch, and resets the work tree. The result has default configuration, no hook, no remote, no untracked or ignored file, and belongs to the step user, so the step container can write to every path.

**The `hephaestus` image has git, and so a newer base image.** The gateway needs git 2.48 or later, which fetches from a bundle with object checking (ADR-0010). The `aspnet:10.0` image is Ubuntu 24.04, whose git is 2.43, so the runtime stage is `aspnet:10.0-resolute` (Ubuntu 26.04, git 2.53), and the build stops when the installed git is older than 2.48.
**The `hephaestus-tools` image is `alpine:3.23` and `git`**, and the build stops on the same check. Compose builds it from `deploy/tools.Dockerfile` in a profile that `up` does not start (`docker compose --profile tools build`); a machine that has not got it has the runtime fetch it.

## Alternatives considered

- **A shallow clone from GitHub inside the checkout container.** Needs a network and a credential in a container the architecture says has neither. Rejected.
- **`git clone` of the mirror by a bind mount or a volume.** The mirrors volume would be mounted into a container that an Agent's work reaches. Rejected: one file crosses, and the container mounts nothing of the host's state.
- **Scrub the volume instead of emptying it and making a repository.** Cleaning is easier to get wrong than making again (ADR-0009).
- **Git in the `hephaestus` image by compiling it, or from a backport.** Ubuntu 24.04 and Debian 13 have no git of 2.48 or later; a source build adds a toolchain stage to pin and verify. A newer base is one line and one check.
- **The gateway runs git in a `hephaestus-tools` container.** The gateway would then need the Docker socket's reach, which only the runtime has, and the token would be in a container's configuration. Rejected.

## Consequences

- Every step pays for one bundle (the whole history of the commit) and one clone. A large repository makes that slow; a shallow bundle would not be a bundle git can verify.
- The first `git fetch` of a Project needs GitHub's address, which the `hephaestus` container reaches directly; it is not behind the egress proxy, which is for step containers.
- A machine with no `hephaestus-tools` image and no registry to fetch it from cannot start a step: `workspace.tools-image.unavailable` names the image and `HEPHAESTUS_TOOLS_IMAGE`.
- The checkout does not check objects strictly: it clones what the mirror holds, which the gateway fetched from the repository the Owner registered. Strict checking is of what comes back, the checkpoint's bundle (Story 3.7).
- The gateway's git gets the proxy and certificate variables of the host's environment (`HTTPS_PROXY`, `HTTP_PROXY`, `NO_PROXY`, their lower-case forms, `SSL_CERT_FILE`, `SSL_CERT_DIR`, `GIT_SSL_CAINFO`) and nothing else of it; the host's own requests to GitHub use the same settings.
- A mirror whose directory is not a whole bare repository (a crash left it half made) is removed and made again, and a garbage collection that git starts runs inside the process, where its timeout reaches it. Nothing prunes the objects of branches that were deleted on GitHub beyond that, and a mirror only grows with the repository.
- Submodules and Git LFS are not handled: a submodule's directory is left empty and an LFS file stays a pointer, because the checkout has no network and no filter (`deferred-work.md`).
- A bundle's size is checked against the bound before it is written, from the disk usage of the objects the commit needs, and again when it is written.
- Publishing `hephaestus-tools` beside `hephaestus`, in the same channel and under the same tag, belongs to the release workflow of Story 1.13, which does not exist yet.
