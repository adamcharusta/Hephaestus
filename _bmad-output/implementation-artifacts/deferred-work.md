- source_spec: `_bmad-output/implementation-artifacts/spec-1-6-mask-secret-values-wherever-text-is-written.md`
  summary: Mask each line of a multi-line secret (a PEM private key) on its own, with both line endings, when Story 1.7 stores one.
  evidence: The masker matches a value whole, so a tool that logs a stored key one line at a time is not masked; no multi-line secret is stored before Story 1.7 (edge-case review, finding 13).
