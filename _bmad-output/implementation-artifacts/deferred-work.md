- source_spec: `_bmad-output/implementation-artifacts/spec-1-6-mask-secret-values-wherever-text-is-written.md`
  summary: Mask each line of a multi-line secret (a PEM private key) on its own, with both line endings, when Story 1.7 stores one.
  evidence: The masker matches a value whole, so a tool that logs a stored key one line at a time is not masked; no multi-line secret is stored before Story 1.7 (edge-case review, finding 13).

- source_spec: `_bmad-output/implementation-artifacts/spec-1-9-define-aliases-limits-and-protected-paths.md`
  summary: Decide whether protected paths match case-sensitively when the gateway enforces them.
  evidence: The list stores globs exactly as typed, so `Hephaestus.yml` is not `hephaestus.yml`; on a case-insensitive checkout the file the default entry protects can be reached under another spelling. The enforcing story defines the matcher and must settle the case policy and add a test for the two default entries.

- source_spec: `_bmad-output/implementation-artifacts/spec-1-10-register-a-project.md`
  summary: Warn at `project add` when the repository is archived or disabled, and settle whether a ruleset-only protection shows in the `protected` flag of the branch.
  evidence: The gateway reads the repository from the installation's list and the branch's `protected` flag; an archived repository can neither be pushed to nor receive pull requests, and nothing in Story 1.10's acceptance criteria asks for the check. Whether GitHub sets `protected` for a branch covered only by a ruleset is unverified against live GitHub (review, findings 4 and 9); the message names the flag and claims nothing about what it covers.
