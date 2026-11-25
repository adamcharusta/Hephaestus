# Inline check of the 2026-11-25 update

**Not an independent review.** The Reviewer Gate calls for a rubric walker and the two configured lenses (web verification, adversary) as separate subagents. They were not dispatched for this update; this file records the author's own pass only.

**Scope:** three owner-confirmed amendments — dependencies in dispatch (AD-1), the egress proxy (AD-7), and edit detection (AD-15) — and the companion documents re-rendered from them.

## Mechanical

- `lint_spine.py`: 0 findings.
- All 17 Mermaid blocks in the spine, `docs/architecture.md`, `docs/architecture-c4.md`, and `docs/security/threat-model.md` parse with Mermaid 11.
- AD ids unchanged (AD-1 to AD-20); no AD added or retired.

## Verified on the web

- Compose documents an internal network as having no outbound route, and a service attached to both an internal and a normal network as the way out.
- GitHub GraphQL: `userContentEdits` is on the `Comment` interface, which `Issue` and `IssueComment` implement; `UserContentEdit.editor` is a nullable `Actor`. Whether a GitHub App installation token can read it is not verified (Open Question 4).

## Findings from the author's adversarial pass

| # | Severity | Finding | Disposition |
| --- | --- | --- | --- |
| 1 | Medium | With an allowlist set, the proxy builder and the configuration-schema builder could disagree on whether an Agent's own vendor hosts are implied. | Added to the Deferred row for the egress proxy (v0.1 execution epic). |
| 2 | Medium | AD-15 says how an edit is detected but not when content is read again. | Deferred to the intake epic. Safe to defer: context files come only from the admitted snapshot (AD-12). |
| 3 | Medium | A merge by a collaborator who is not a Trusted User: does the Task reach `Merged`? Dependents wait on the answer. The ambiguity predates this update; dependencies in v0.1 make it matter. | Open Question 5. |
| 4 | Low | Step containers "cannot reach each other", yet all must reach the proxy on the same network. | Named in the Deferred row; Docker object labels already allow a network per step run. |
| 5 | Low | A tool that cannot use an HTTP proxy has no network access from a step container. | Recorded as a consequence in ADR-0008 and as a known limitation in the threat model. |
| 6 | Low | The proxy is conditional on the injection spike; no fallback is named if a CLI does not work through `HTTPS_PROXY`. | Left open on purpose: the spike result decides. |

## Second update, same day

The owner then decided findings 1, 3, and 5:

| # | Decision | Where it landed |
| --- | --- | --- |
| 1 | An Agent's vendor hosts are declared by its adapter and allowed only in that Agent's container; allowed hosts are per step run. | AD-7, AD-10 |
| 3 | A merge by someone who is not a Trusted User cancels the Task; a Trusted User's `confirm` on the pull request accepts it. Open Question 5 closed. | AD-2, AD-15, lifecycle diagram, threat T33 |
| 5 | The limitation stays, softened: proxy variables with loopback excluded, SSH-to-HTTPS rewrite for `github.com`, refusals recorded on the step run, toolchain matrix in the injection spike. | AD-7, AD-18, Open Question 1 |

Author's pass on these three:

| # | Severity | Finding | Disposition |
| --- | --- | --- | --- |
| 7 | Medium | A collaborator with write access who is not a Trusted User can now cancel a Task by merging its pull request, which bends AD-2's own "an untrusted user cannot stop a Task". | Accepted by the decision; stated as the one exception in AD-15, ADR-0003, and threat T33. |
| 8 | Medium | A Task cancelled by an untrusted merge can still be opted in again, with a pull request that is already merged. | Deferred to the intake epic with the unsatisfiable-dependency row. |
| 9 | Low | How the runtime collects the proxy's refusals is not fixed; only that they travel on the step-exited Signal. | Deferred row for the egress proxy. |
| 10 | Low | The SSH rewrite covers `github.com` only. | Deferred row for the egress proxy. |
| 11 | Low | A pull request merged while its Task is `Queued` or `Running` (after a Rework or a second opt-in) has no edge in the lifecycle diagram. Predates both updates. | Not addressed; for the v0.3 epic, where Rework arrives. |

## Not checked

- No proxy product was evaluated; none is named.
- The prose and structure review configured as `doc_standards` was not run on the re-rendered companion documents.

## Third update (2026-11-25): Dashboard in v0.1

Author pass only. Findings 12 to 16, all applied.

12. **Revocation and open streams.** FR-68 says a revoked token stops working at once; a stream opened before the revocation would have kept flowing. AD-17 now closes every stream opened with a revoked token.
13. **Reads had no named path.** The spine named `IUnitOfWork` as the loop's alone and said nothing about how an endpoint reads. With ten dashboard stories ahead, two builders could read through different paths. AD-17 and AD-3 now name `IReadStore` on a read-only connection.
14. **The objection recorded in ADR-0015** (a localhost TCP listener can be reachable from other containers) was not answered by the proposal as written. AD-17 now forbids the `hephaestus` container from sharing a Docker network with a step container or the egress proxy.
15. **"No module acts on a timer" (AD-1) against a long-lived stream.** AD-17 states that keeping a stream open is transport.
16. **"local API" renamed "control API"** throughout the spine, so that "the API" never means the dashboard listener by accident.

Not done: the independent rubric walker and the two configured reviewer lenses were not dispatched, as in the earlier updates. The dashboard listener is the part of this update that most deserves an adversarial reading before Story 1.14 starts. Mermaid blocks were checked by eye, not by a parser.
