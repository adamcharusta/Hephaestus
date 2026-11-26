- source_spec: `_bmad-output/implementation-artifacts/spec-1-6-mask-secret-values-wherever-text-is-written.md`
  summary: Mask each line of a multi-line secret (a PEM private key) on its own, with both line endings, when Story 1.7 stores one.
  evidence: The masker matches a value whole, so a tool that logs a stored key one line at a time is not masked; no multi-line secret is stored before Story 1.7 (edge-case review, finding 13).

- source_spec: `_bmad-output/implementation-artifacts/spec-1-9-define-aliases-limits-and-protected-paths.md`
  summary: Decide whether protected paths match case-sensitively when the gateway enforces them.
  evidence: The list stores globs exactly as typed, so `Hephaestus.yml` is not `hephaestus.yml`; on a case-insensitive checkout the file the default entry protects can be reached under another spelling. The enforcing story defines the matcher and must settle the case policy and add a test for the two default entries.
