---
title: 'Story 3.1: Pin, fetch, and verify the Agent CLIs'
type: 'feature'
created: '2026-11-27'
status: 'done'
baseline_commit: '138c1585d5a3c1690db2a0309bf24d6e6807a86e'
route: 'dispatch'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Hephaestus has no Agent CLI: nothing pins the versions the adapters were tested with, nothing brings them to a step container, and Claude Code may not be redistributed in an image (AD-8, ADR-0011, AR-17, AR-40, T29).

**Approach:** `deploy/agents.lock` pins one version of each CLI and the SHA-256 of every executable, per architecture and C library, and the archive digest where the vendor ships an archive. After the database is migrated and before the loop's first tick, the host fills the `hephaestus-agents` volume from the vendors' official channels, verifies every executable, replaces a CA bundle copied from the image's own store, and stops, naming the file, if anything fails. A check in the repository's CI verifies each pin against what the vendor publishes and signs, and the executables are started on both architectures.

## Boundaries & Constraints

**Always:**
- The image carries the lock and no Agent binary. No Hephaestus base image is built.
- A download is checked against the vendor's digest, an executable taken out of an archive against its own digest, and only then moved into the volume. A failure leaves nothing unverified, including a file that was there before.
- Startup stops unless every executable the lock names for the host's architecture is present with its digest. Nothing is downloaded when they are.
- The vendor's signature is verified when a pin changes (CI), not at startup.
- Every sentence for a person is a message id in `en` and `pl`.

**Never:**
- Choose the C library of a Project image, mount the volume into a step container, or run a container (Story 3.2); adapters (3.8, 3.9); the sign-in modes (3.10).
- Close a change of pin without the credentialed runs of `docs/spikes/injection`; that is documented, not done here.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| First start | Empty volume, vendors reachable | Every executable of the architecture downloaded, verified, in place; CA bundle copied; loop starts | N/A |
| Second start | Pinned, verified files present | Nothing downloaded; CA bundle replaced | N/A |
| Wrong download | Archive or executable digest differs from the lock | Startup stops, names the executable, address, both digests; nothing from it kept | Exit 1, message on standard error |
| Tampered file | A file with another digest, or a link | Removed, fetched again; if that fails it stays removed | As above |
| Stray file | Anything the lock does not name | Removed | N/A |
| Bad archive | Not one regular file, or not gzip | Startup stops naming the executable | As above |
| Unreachable vendor | Connection refused, HTTP error, timeout | Startup stops naming the executable and address | As above |
| Bad lock | Missing, unreadable, incomplete, or invalid line | Startup stops naming the file, and the line | As above |
| Pin change | A pull request edits `deploy/agents.lock` | CI fails unless digests and signatures verify | N/A |

</frozen-after-approval>

## Code Map

- `deploy/agents.lock` -- the lock; format in its header and `docs/agent-clis.md`. Digests of the executables inside Codex's archives were computed from the vendor's archives and verified with cosign.
- `src/Hephaestus.Core/AgentBinaries/AgentLock.cs` -- pure parser; demands the whole matrix; `Message`-carrying `AgentLockException`. The namespace is not `Hephaestus.Core.Agents`: it would hide the `Agents` class of `Hephaestus.Core.Aliases`.
- `src/Hephaestus.Workspaces/AgentBinaryVolume.cs` -- check-or-fetch, staging inside the volume, tar extraction, prune, CA bundle. Hosts no logger: results come back and through a callback.
- `src/Hephaestus.Host/Hosting/AgentBinaryStartup.cs`, `EntryPoint.cs`, `HostSettings.cs` -- the startup step, its sentence, the three settings; the Host project places the lock beside the executable.
- `deploy/Dockerfile`, `deploy/compose.yaml`, `.dockerignore` -- the lock in the image, the volume, the long health start period.
- `eng/verify-agents-lock.py` (+ tests), `eng/check-agent-binaries.sh`, `.github/workflows/ci.yml` -- vendor verification, hardened start on both architectures, no download on a second start, no binary in the image.
- `docs/agent-clis.md`, `README.md`, `deploy/README.md`, `docs/branching.md`.

## Tasks & Acceptance

**Execution:**
- [x] Lock, parser, messages, tests.
- [x] Volume preparation, tests with the vendors in memory.
- [x] Host startup step, settings, end-to-end tests with the real executable.
- [x] Image, Compose, CI, verification script and its tests.
- [x] Documentation, including how a pin is changed.

**Acceptance Criteria:**
- Given each acceptance criterion of Story 3.1 in `epics.md`, then a test, a CI step, or a document shows it.

## Implementation Notes

- Unattended run: no human gate. Verified by hand against the real vendors on arm64: first start downloaded and verified four executables in about 90 seconds, a second start downloaded nothing, a stray file was removed, a wrong digest and an unreachable vendor stopped startup naming the file, and nothing unverified stayed in the volume. `eng/verify-agents-lock.py` passed against the live Claude Code manifest and the Codex release.
- Docker is not available where this was built, so the container steps (hardened start, image contents, restart) are CI steps that this run could not execute.
- Layout in the volume: `<agent>/<arch>-<libc>/<file>`; `static` is the libc of Codex CLI. The runtime story chooses the set that matches a Project image.
- The files are mode 0555 and the directories 0755: a step container runs as a user of its own.

## Verification

**Commands:**
- `dotnet build Hephaestus.slnx` -- expected: no warnings
- `dotnet test --solution Hephaestus.slnx` -- expected: all pass
- `python3 eng/check-coverage.py TestResults` -- expected: line coverage at or above 80%
- `python3 -m unittest discover -s eng -p "test_*.py"` -- expected: all pass

## Review Triage Log

| Finding | Verdict | Route | Evidence |
|---------|---------|-------|----------|
| Restart policy re-downloads after a stopped start | medium | patch | Real: `unless-stopped` restarts a stopped host. The documentation now says so and what to do; the spec requires that startup stops. |
| `start_period` against the 30-minute download limit | low | none | An unhealthy status does not stop a container; the documentation says the container is `starting` during the first fill. |
| Redirect host of GitHub downloads not named | medium | patch | Confirmed: `release-assets.githubusercontent.com`; named in `docs/agent-clis.md`. |
| Story `done` while container CI steps have not run | false | none | The run's instructions require `done` in this pull request; the CI steps run on the pull request and gate its readiness. |
| Musl build or `--help` of the host may not start on bare Alpine | false | none | The spike ran both on `alpine:3.23` with `bash` only; `codex-code-mode-host --help` was run and prints `Usage:`. |
| Hardened check lacks `--read-only`, differs from the spike | low | none | It checks that an executable starts under the restrictions that matter to it; Story 3.2 owns the step container. |
| Verifier crashes on `gpg --import` or a malformed release | medium | patch | Import failure is a `LockError`; `main` reports other errors as lines. Tests added. |
| Real `gpg`/`cosign` paths untested | medium | patch | Tests use a key made for the test and a faked `cosign` process. |
| Python parser weaker than the C# one | low | none | The committed lock is held to the full matrix by the Host tests; the script only reads that lock. |
| Pre-release versions are refused | low | patch | Documented in the lock header and `docs/agent-clis.md`. |
| Symlinked ancestor directory passes for an executable | medium | patch | Prune now runs before the check, so a link is removed first; test added. |
| Downloads have no size bound | low | none | The digest decides; a bound needs a size column in the lock for a source that is already the vendor over TLS. |
| No progress during the first start | low | none | Each executable is logged as it is verified; the spec puts the fill before the API listens. |
| `Authorization` forwarded on redirect | low | patch | `add_unredirected_header`. |
| Test hangs if loopback drops packets | false | none | Loopback refuses at once; no test depends on a remote host. |
| Reused file keeps a wrong mode | medium | patch | The mode is set when the digest matches; test added. |
| Disk error while extracting reported as a bad archive | low | patch | Only `InvalidDataException` and `EndOfStreamException` are an archive fault. |
| Empty CA bundle copied | low | none | The source is the image's own certificate store. |
| Expired or revoked signing key accepted | medium | patch | `GOODSIG` is required and `BADSIG`, `ERRSIG`, `REVKEYSIG` refuse; test with a local key. |
| Image job timeout shorter than the waits | medium | patch | Raised to 50 minutes. |
| "The message gives the line" for a missing pin | low | patch | Wording corrected. |
| Order of migration and fetch not pinned by a test | low | patch | The failing-download test asserts the database exists. |
| Download timeout untested | medium | patch | The limit is injectable; test added. |
