---
title: 'Story 1.6: Mask secret values wherever text is written'
type: 'feature'
created: '2026-11-26'
status: 'done'
baseline_commit: '6821350c37e009b24d2d84fc99070929ab1b40dc'
route: 'dispatch'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** A credential that a tool echoes can reach the host's log, because nothing between a log call and standard output knows the stored secret values (FR-4, AD-16, T16).

**Approach:** Add one masker to Core, built from `ISecretStore` and rebuilt when a secret changes. It replaces each value, and its base64 and URL-encoded forms, by a mask; it masks a value that a chunk boundary splits. The host's logger writes every line through it.

## Boundaries & Constraints

**Always:**
- One masker service, `SecretMasker`, defined in Core, built from `ISecretStore` (AR-36); an architecture test pins it. Core gains no EF Core, SQLite, or ASP.NET Core reference.
- A value shorter than the policy's minimum (8) is never masked, so ordinary text is not.
- A secret that is added, changed, or removed is masked, or released, on the next line, without a restart; the store reports a change and waits for the masker before it returns.
- Text is masked before it is JSON-encoded, so a value that JSON would escape is still found.
- Line coverage stays at or above 80% overall and on new code; no new package.

**Never:**
- No masking of the runtime's raw output stream (`IOutputStore`) or the gateway's outbound bodies and pushes: those ports do not exist yet and apply this masker when they are built (AD-9, AD-16).
- No change to `_bmad/`, skills, `SPEC.md`, the architecture, `epics.md`, or an ADR.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior |
|----------|--------------|---------------------------|
| Plain | a log line holds a stored value | stdout line holds `***` in its place |
| Encoded | the line holds the value's base64 (padded or not) or URL-encoded form | the same |
| Split | a value is split across two chunks, at any position | the masked output holds no part of it |
| Change | a secret is set, replaced, or removed while the host runs | the next line masks the new value, or no longer masks the removed one |
| Start | secrets already on the volume | masked from the first line after start |
| Overlap | two values overlap, or one begins the other | one masked run; no tail left |
| Short | a stored file holds fewer than 8 characters | nothing is masked |
| Architecture | the code base | exactly one masker, in Core, built from `ISecretStore`; one console provider whose formatter takes it |

</frozen-after-approval>

## Code Map

- Spine AD-16 (the masker), AR-36, Logging convention; epics Story 1.6; threat model T16 (exact, base64, URL-encoded only; split values by the chunk-aware stream).
- `src/Hephaestus.Core/Secrets/` -- `SecretMasker` (rebuild, `Mask`, `CreateStream`), `SecretMatcher` (the forms, the scan that holds back a possible beginning), `SecretMaskingStream`; `Ports/ISecretStore` gains `ReadAllAsync` and `Changed`.
- `src/Hephaestus.Persistence/FileSecretStore.cs` -- lists every value, raises `Changed` (awaited, under the gate) after a set or a removal.
- `src/Hephaestus.Host/Hosting/` -- `MaskedJsonFormatter` replaces the framework's JSON console formatter; `ControlHost` registers the masker and loads it before the host listens.

## Tasks & Acceptance

**Execution:**
- [x] `src/Hephaestus.Core/**` -- masker, matcher, chunk stream, store port additions
- [x] `src/Hephaestus.Persistence/FileSecretStore.cs` -- `ReadAllAsync`, `Changed`
- [x] `src/Hephaestus.Host/**` -- masked JSON formatter, registration, load before listening
- [x] `tests/**` -- Core (forms, splits, overlap, rebuild), Persistence (change notification), Host (formatter, architecture), end to end (real stdout)
- [x] `README.md` -- what the logs hold of a secret

**Acceptance Criteria:**
- Given a stored value, when a log line holds it plain, base64, or URL-encoded, then the line on stdout holds a mask.
- Given a value split across two chunks, then the masked output holds no part of it.
- Given a running installation, when a secret is added, changed, or removed, then the masker is rebuilt and the new value is masked without a restart.
- Given the code base, then an architecture test shows exactly one masker, in Core, built from `ISecretStore`, and the logger writes through it.

## Implementation Notes

- 2026-11-26. Built directly in one unattended session; no question was put to the Owner. The choices the story leaves open: the mask is `***`; the forms are plain, base64 with and without padding, and URL-encoded by `Uri.EscapeDataString` and by `WebUtility.UrlEncode` (a space as `+`); overlapping forms are one masked run.
- `ISecretStore.Changed` is an awaited event, raised inside the store's gate, so the masker has caught up before `secret set` answers. The masker subscribes in its constructor; the host builds it before the first log line and loads it after the migrations and before it listens, and a failure to load stops the host.
- The framework's JSON console formatter cannot be given a hook, so `MaskedJsonFormatter` writes the same shape (`Timestamp`, `EventId`, `LogLevel`, `Category`, `Message`, `Exception`, `State`, `Scopes`) with each string masked first.
- A secret file edited by hand on the volume is noticed at the next change or restart, not before.
- Review changed three things in the notes above: the masker loads before the migrations; the value without a trailing line break is a form too; numbers, booleans, and null keep their JSON type in the log.

## Spec Change Log

## Review Triage Log

Review pass 1, 2026-11-26. Sources: BH = Blind Hunter, ECH = Edge Case Hunter, VG = Verification Gap.

| # | Finding | Verdict | Evidence | Route |
| --- | --- | --- | --- | --- |
| 1 | The masker loaded after the migrations, so lines logged before were unmasked | medium | BH, ECH; read `MigrateAsync`: `ReadAllAsync` does not touch the database | patch: load first |
| 2 | A failed name write or a failed rebuild left the new value unmasked | medium | BH, ECH, VG; the notification came after the database write | patch: notify in a `finally`; `ReadAllAsync` skips a file it cannot read; test drops the table |
| 3 | Replacing a value through the real store had no test of `Changed` or the masker | medium | VG; only the fake store replaced | patch: test |
| 4 | A hand-placed file ending in a line break never matched a log line; a white-space value masked white space | low | BH, ECH | patch: the trimmed form too; white-space values ignored; tests |
| 5 | State and scope values were all written as strings | low | BH, ECH; the framework's formatter writes numbers, booleans, null | patch: types kept; test |
| 6 | Category and keys were not masked | low | BH, ECH | patch: masked; test |
| 7 | `ReadAllAsync` could be called by anything; only the masker should | low | BH | patch: interface text; architecture test over the source |
| 8 | The writer scan allowed `Console.Out` | low | BH, ECH, VG | patch: scan forbids it outside `Program.cs` |
| 9 | README omitted the limits (hand edits, short values, only host log lines) | low | BH | patch: README |
| 10 | A handler that hangs holds the store's gate | low | BH, ECH | patch: the port says handlers are quick and must not call the store; no timeout, because the one handler reads a few small files |
| 11 | The masker never unsubscribes | low | BH | rejected: the masker and the store are singletons of one lifetime |
| 12 | base64url, lowercase percent-encoding, a value inside a larger base64 string are not caught | low | BH | rejected: the story and T16 name exact, base64, and URL-encoded forms; the README says other encodings are not caught |
| 13 | A multi-line secret logged one line at a time is not masked | medium | ECH | defer: no stored secret is multi-line before Story 1.7; `deferred-work.md` |
| 14 | A held tail waits for the next chunk | low | ECH | rejected: `Complete` exists for the end of a stream, and the doc says so |
| 15 | No concurrency or performance test of the scan | low | BH | rejected: the snapshot is immutable and swapped by one volatile write |
| 16 | A test of the host stopping when the masker cannot load | low | VG | rejected: the failure is an exception from `MigrateAsync`, which the migration test already shows stops `serve` |
| 17 | The sprint status and the spec status disagree | false | the controller requires `done` in this pull request; the spec is `done` now | rejected |

## Verification

**Commands:**
- `dotnet build Hephaestus.slnx` -- expected: 0 warnings, 0 errors
- `dotnet test --solution Hephaestus.slnx --coverlet --coverlet-output-format cobertura --results-directory TestResults` then `python3 eng/check-coverage.py TestResults` -- expected: all pass, coverage at or above 80%
- `python3 eng/check-licenses.py` -- expected: pass
