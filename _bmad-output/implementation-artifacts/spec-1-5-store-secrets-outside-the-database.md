---
title: 'Story 1.5: Store secrets outside the database'
type: 'feature'
created: '2026-11-26'
status: 'done'
baseline_commit: 'e46102192cf64c933f6aac65b65b8c29216a6460'
route: 'dispatch'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** The Owner cannot hand Hephaestus a credential: there is no database, no secret store, and no `secret` command, so nothing can later be masked or handed to a step (FR-4, AD-16, AD-17, T16, T17).

**Approach:** Add the SQLite database (one `DbContext`, one migration set, WAL, busy timeout, UUID v7 text ids, one UTC instant converter) and `IReadStore` on a read-only connection. Add `ISecretStore`: a value is a file on the `hephaestus-secrets` volume, the database holds the name. Add the control endpoints and the `secret set | list | remove` commands; the value arrives on standard input only.

## Boundaries & Constraints

**Always:**
- A secret value is never in the database, a log line, an API response, a command line, or a problem document. The minimum length is 8 characters, the maximum 65,536; a name is 1 to 64 characters of `a-z`, `0-9`, `-`, `_`, starting with a letter or digit, and is the file's name.
- `hephaestus-secrets` is mounted into the `hephaestus` service alone, at a directory only the process user can enter; a file is written with mode 0600, through a temporary file and a rename.
- Writes (`PUT`, `DELETE`) are mapped on the control socket only; the list reads through `IReadStore`, never `IUnitOfWork`. Core gains `ISecretStore`, `IReadStore` and the secret rules, and no EF Core, SQLite, or ASP.NET Core reference.
- Every sentence is a message id in `en` and `pl`; a refusal is a problem document that never contains the value.
- One trailing line break of the standard input is dropped, so `echo value |` works; nothing else is changed.
- Packages only at the Stack pin (EF Core Sqlite 10.0.15); line coverage stays at or above 80% overall and on new code.

**Never:**
- No masker (1.6), no pre-migration backup (1.12), no dashboard listener (1.14), no GitHub identity (1.7), no Installation Configuration tables beyond secret names.
- No value as a command-line argument, as an option, or in a route; the parser's refusal of one never echoes it.
- No change to `_bmad/`, skills, `SPEC.md`, the architecture, `epics.md`, or an ADR.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Set | `secret set <name>`, value on stdin | file written; name recorded; stdout: sentence naming the secret (`--json`: `{"name":…}`); exit 0 | N/A |
| Replace | name already stored | file replaced; one name row remains; exit 0 | N/A |
| Argument | `secret set <name> <value>` or `--value …` | stderr: refusal naming no token; exit 2; nothing stored | N/A |
| Short or empty | value under 8 characters, empty, or blank | problem `secret-value-too-short` or `secret-value-empty`; exit 1; nothing stored | Detail names the minimum, not the value |
| Too long | value over 65,536 characters | problem `secret-value-too-long`; exit 1 | N/A |
| Bad name | name outside the rule | problem `secret-name-invalid`; exit 1; no file | Detail states the rule, not the name |
| List | `secret list` | names only, a table with a header; `--json`: `{"secrets":[{"name":…}]}`; none: a sentence | N/A |
| Remove | `secret remove <name>` | file and name gone; stdout: sentence naming it; exit 0 | Unknown name: problem `secret-not-found`, exit 1 |
| Restart | host started again on the same volumes | list and `ISecretStore` still return every secret | N/A |
| First start | empty data volume | database created in WAL mode with busy timeout; migrations applied; ids UUID v7 text, instants fixed-width UTC text | A migration failure stops startup |

</frozen-after-approval>

## Code Map

- Spine AD-16 (secrets as files, names in the database), AD-17 (control listener, `IReadStore`), AR-30/32/35/39, EXPERIENCE "Command Line"; epics Story 1.5; threat model T16, T17.
- `src/Hephaestus.Core/` -- gains `Secrets/SecretPolicy`, `Ports/ISecretStore`, `Ports/IReadStore`, `Identity/EntityId`; message ids and both resource files grow.
- `src/Hephaestus.Persistence/` -- EF Core Sqlite; `HephaestusDbContext`, converters, `Migrations/`, `SqliteReadStore`, `FileSecretStore`, `DatabaseMigrator`, `PersistenceServiceCollectionExtensions`.
- `src/Hephaestus.Host/` -- `HostSettings` (data and secrets directories), `Api/ControlApi` (secret endpoints), `Api/ProblemResponses`, `Cli/` (commands, stdin, client verbs), `Hosting/ControlHost`, `EntryPoint` (migrate before serving).
- `tests/Hephaestus.Host.Tests/Architecture/BoundaryRules.cs` -- EF Core and SQLite stay in Persistence; keep passing.
- `deploy/compose.yaml`, `deploy/Dockerfile`, `.github/workflows/ci.yml`, `README.md`, `deploy/README.md` -- volumes, directories, the CI image job, the reader's commands.

## Tasks & Acceptance

**Execution:**
- [x] `src/Hephaestus.Core/**` -- secret rules, ports, UUID v7 helper, message ids and en/pl entries -- one place for the rules; sentences in resources
- [x] `src/Hephaestus.Persistence/**` -- context, converters, initial migration, read store, file secret store, migrator, DI extension -- AR-30, AR-32, AR-35, AD-16, AD-17
- [x] `src/Hephaestus.Host/**` -- settings, secret endpoints and problems, `secret` commands reading standard input, serve migrates first -- AD-17, UX grammar
- [x] `deploy/**`, `.github/workflows/ci.yml` -- two volumes, directories owned by the process user, CI checks for set, list, restart, remove, and the volume's mounts
- [x] `tests/**` -- Core rules; Persistence (WAL, busy timeout, converters, migration, store, restart, no value in the database file); Host (API, CLI, never-in-output search over database, logs, and responses); compose contract
- [x] `README.md`, `deploy/README.md` -- the secret commands and the volumes

**Acceptance Criteria:**
- Given a running installation, when a secret is set from standard input, then a file holds the value on the secrets volume, the database holds only the name, and a value given as an argument is refused.
- Given a known value, when the database files, the host's logs, and every response of set, list, and remove are searched, then it is found in none.
- Given stored secrets, when the host restarts, then they are still available; when one is removed, its file and name are gone.
- Given an empty data volume, when the host starts, then one WAL-mode SQLite database with a busy timeout exists and its migrations are applied.

## Implementation Notes

- 2026-11-26. Built directly in one unattended session; no question was put to the Owner, so the choices the story leaves open are made here: a value has 8 to 65,536 characters; a name is the file's name, 1 to 64 characters of `a-z`, `0-9`, `-`, `_`, starting with a letter or digit; one trailing line break of standard input is dropped; a value holding U+FFFD (bytes that were not UTF-8) is refused.
- A secret name row has an id (UUID v7 text), the name, and the instant it was created (fixed-width UTC text), so that both conventions of AR-30 and AR-32 are exercised by the first table. The value is never a column.
- The control API carries `GET /api/secrets` (through `IReadStore`), `PUT /api/secrets/{name}` (the value is the plain-text body, never an address), and `DELETE /api/secrets/{name}`. A name that is `.` or `..` is collapsed by the HTTP client's URI normalisation and answers 404; nothing is written.
- The directories are `/var/lib/hephaestus/data` and `/var/lib/hephaestus/secrets` (`HEPHAESTUS_DATA_DIR`, `HEPHAESTUS_SECRETS_DIR`), the mount points of the volumes `hephaestus-data` and `hephaestus-secrets`. The store sets the secrets directory to mode 0700 and each file to 0600 itself, so the claim does not rest on how a volume was made.
- A write puts the file down first and the name second; a removal takes the file first and the name second. A leftover temporary file from a host that died mid-write is swept by the next write.
- The initial migration was generated with `dotnet ef` 10.0.15 (a temporary reference to `Microsoft.EntityFrameworkCore.Design`, removed again) and then edited: `#nullable disable` and `#pragma` lines removed (a build rule test forbids them), file-scoped namespace. A later migration generated by the tool must be edited the same way.
- Not verified here: no Docker daemon was available, so the new steps of the CI `Image` job (secret set, list, restart, remove, volume mounts, no value in the data directory or the logs) ran only as reviewed shell; the in-process and subprocess tests cover the same behaviour.
- The pre-migration backup (Story 1.12) and the masker (Story 1.6) are not part of this story.

## Spec Change Log

## Review Triage Log

Review pass 1, 2026-11-26. Sources: BH = Blind Hunter, ECH = Edge Case Hunter, VG = Verification Gap.

| # | Finding | Verdict | Evidence | Route |
| --- | --- | --- | --- | --- |
| 1 | `secret set token --json=<value>` printed the value in the not-understood message | medium | ECH; reproduced: the parser reads it as `--json` plus the argument `<value>` | patch: any option `secret set` does not have, any parse failure, or any leftover token refuses without naming a token; tests |
| 2 | A 65,536-character value piped with CRLF was refused (buffer cut the line break) | low | ECH, VG, BH-claim; read the buffer size | patch: the buffer holds the line break; test |
| 3 | `secret list` crashed on `{}`, `{"secrets":null}`, `[null]` answers | low | ECH; null dereference | patch; tests |
| 4 | Bytes that are not UTF-8 are replaced by U+FFFD and stored as the credential | medium | BH, ECH | patch: a value holding U+FFFD is refused (`secret-value-not-text`); tests |
| 5 | No test pipes non-ASCII text through the real executable | medium | VG | patch: end-to-end test compares the file's bytes |
| 6 | A temporary file left by a dead host holds a plaintext value nothing lists or removes | medium | BH, ECH | patch: the next write sweeps them; test |
| 7 | The value is flushed to the OS cache, not to disk, before the rename | low | BH, ECH | patch: `Flush(flushToDisk: true)` |
| 8 | The data directory is created with the default umask mode | low | BH, ECH | patch: created 0700 when the host creates it |
| 9 | The bounded read has no test that observes the bound | low | VG | patch: test over an endless stream |
| 10 | Backup advice without stopping the container (WAL) | low | BH | patch: README says to stop the container first |
| 11 | `secret set <name> --jsno` gets the value-as-argument message | low | BH, VG | rejected: the message cannot be more specific without naming a token; the exit code is the same |
| 12 | Other verbs (`remove`, `list`) echo an unmatched token | low | BH | rejected: the story's refusal concerns the value of `secret set`; the token is what the Owner just typed |
| 13 | `.` and `..` as a name answer a generic 404 | low | ECH | rejected: URI normalisation happens before the request; nothing is written and the exit code is 1 |
| 14 | `set` does not say whether it replaced a secret | low | BH | rejected: the story says set stores; replacing is the same operation |
| 15 | A failed database write after a replace leaves the new file active | low | ECH | rejected: the name row already exists for a replace, so nothing is orphaned |
| 16 | `ReadAsync` lets `UnauthorizedAccessException` escape for a directory or unreadable file | low | ECH | rejected: needs a state the host never creates |
| 17 | `-it` without input waits for end of input | low | ECH | rejected: the README says to pass `-i` and pipe the value; a prompt is forbidden by the grammar |
| 18 | `--help` with another parse error now exits 0 | low | ECH | rejected: help wins over a missing argument by design; the case named has no real use |
| 19 | The name length is in a constant and in the regex | low | BH | rejected: tests pin 64 and 65 |
| 20 | `Persistence.csproj` has a package without a version | false | BH read a diff that omitted `Directory.Packages.props`; the pin is already there (10.0.15) | rejected |
| 21 | Migration files absent from the diff | false | BH; they were excluded from the diff file on purpose | rejected |
| 22 | Windows reserved names, directory fsync, in-place upgrade CI step | low | BH | rejected: Linux container only; no behavior shown to fail |

## Verification

**Commands:**
- `dotnet build Hephaestus.slnx` -- expected: 0 warnings, 0 errors
- `dotnet test --solution Hephaestus.slnx --coverlet --coverlet-output-format cobertura --results-directory TestResults` then `python3 eng/check-coverage.py TestResults` -- expected: all pass, coverage at or above 80%
- `python3 eng/check-licenses.py` -- expected: pass
- `docker compose -f deploy/compose.yaml config` -- expected: valid
