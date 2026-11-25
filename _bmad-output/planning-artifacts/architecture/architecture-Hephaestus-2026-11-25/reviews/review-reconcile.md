# Reconciliation review: ARCHITECTURE-SPINE.md against its inputs

- **Spine:** `_bmad-output/planning-artifacts/architecture/architecture-Hephaestus-2026-11-25/ARCHITECTURE-SPINE.md` (status draft, 17 ADs)
- **Inputs read in full:** `prd.md`, `addendum.md`, `research.md`, `init.md`
- **Also consulted (not an input):** the run's `.memlog.md`, only to tell "decided but did not land in the spine" apart from "never considered"
- **Date:** 2026-11-25

## Verdict

The spine is faithful to the inputs on its big structural bets (single writer, effect journal, gateway, bundle boundary, trust on the way in, config snapshot) and contradicts them in only three places, but about ten quiet requirements are "bound" by an AD header or the capability map without any rule text that would actually make a builder honour them.

## How to read the marks

| Mark | Meaning |
| --- | --- |
| **missing** | The input requires it, it belongs at spine altitude (crosses modules or is a "never"), and no rule, convention, seed, or Deferred row carries it. |
| **contradicted** | Spine text says something an input says otherwise, and the PRD did not override the input. |
| **silently dropped** | An input recommended or handed it over; the spine neither adopts it nor defers it by name. |
| **fine-but-worth-a-line** | Landed, or legitimately feature-internal, but one sentence would remove an ambiguity a builder will otherwise resolve alone. |
| **ok** | Landed; listed only where the task asked for an explicit status. |

A recurring pattern: an AD lists an FR under **Binds** while its **Rule** never states that FR's consequence. Affected: FR-14 and FR-15 (AD-1), FR-22 (AD-6), FR-26 (AD-8), FR-44 (AD-9), NFR-6 (AD-4). Each is itemised below.

---

## 1. PRD (`prd.md`)

### 1.1 Missing or contradicted

**P-1. FR-22 Safe cleanup. missing.**
AD-6 binds FR-22, but no rule says when a Task volume may be removed. The PRD has two testable "nevers": a Workspace with work not yet in the repository is never removed automatically, and removal touches nothing outside it. AD-5's effect list covers container and volume creation, not removal.
*Proposed fix (add to AD-6):* "A Task volume is removed only when the Task has ended and the journal shows the last checkpoint tip as pushed; otherwise it is kept and reported. Removal is a journaled effect and selects objects only by the `hephaestus.task` label of that Task."

**P-2. FR-26 availability check, FR-28 "cannot continue", NFR-12 outage tolerance. contradicted (AD-11) and missing (AD-10, AD-1).**
- AD-11 ends "Only `usage-limit` may lead to Handoff or waiting for capacity." FR-26 says an unavailable Agent "is treated as having no capacity", which leads to exactly those two outcomes. FR-28 triggers Handoff when the Agent "cannot continue", which is wider than a Usage Limit.
- AD-10 says an adapter does "four things"; none is an availability probe. FR-26 is v0.1, while the only related Deferred row ("capacity probing") is v0.2. Because adapters start no process, the probe has to be an invocation the adapter describes and the runtime runs in a container holding that Agent's credential. That is a cross-module contract and belongs in the spine.
- NFR-12 ("retries later without losing state" when GitHub or a provider is unreachable) has no home. There is no waiting state or resume condition for an outage, AD-5 describes resolving unfinished effects only "on startup", and nothing says an outage must not consume a step's attempt bound or a Correction round. As written, a GitHub outage walks a Task to `Failed`, whose outcome is a draft pull request that cannot be opened.
*Proposed fix:* (a) AD-10: add a fifth duty, "describes an availability probe and interprets its result". (b) AD-11: replace the last sentence with "An Agent has no capacity when its last failure is `usage-limit` or its availability probe fails; nothing else leads to Handoff or waiting for capacity." (c) AD-1 or a convention row: "An `infrastructure-error` caused by an unreachable provider or GitHub puts the Task in a wait with a time-based resume condition and backoff; it does not count against step attempts or Correction rounds. An effect that fails at run time is resolved by the same natural-key lookup as at startup before it is retried."

**P-3. NFR-5 and FR-3: where limits live. missing.**
AD-6 says limits are "set by the runtime"; the Bounds convention says limits come "from configuration". Neither says which configuration. The PRD is specific: limits are Installation Configuration (Glossary), per Project (NFR-5), and "no change to any repository can ... raise a limit" (FR-3). The Correction Loop bound, by contrast, is Project Configuration. A builder can today put `timeoutMinutes` in `hephaestus.yml` without breaking any spine rule. The addendum (round 6) already enumerated the split; the enumeration did not land.
Related: NFR-5 asks for a limit on **Task duration**, which is not the same as the per-container time limit in AD-6.
Related: the Base Branch is defined inside the file that is "read only from the Base Branch" (PRD Glossary); the spine inherits this circularity without resolving it.
*Proposed fix (convention row "Configuration placement"):* "`hephaestus.yml`: default Agents and Effort, Methodology, Validation commands, stacking mode, Correction Loop bound, image. Installation Configuration, per Project: the Base Branch the file is read from (fixed at registration), Schedule, time, processor, memory, Task-duration and spend limits, egress allowlist. Installation-wide: Trusted Users, Aliases, credentials, Notification channels. A repository value can never raise an installation limit."

**P-4. NFR-4 network egress. contradicted.**
NFR-4: "Outbound network access from a Workspace can be restricted per Project to an allowlist. In v0.1 access is open **unless a list is set**." The Deferred row postpones the whole mechanism to "before general announcement", so in v0.1 no list can be set. That is a scope reduction of a cross-cutting requirement, not a deferral of detail, and it is not recorded as a departure.
It also weakens two "Prevents" lines: with open egress, repository code run by the Agent during an Implementation step can read that Agent's credential from the environment and send it out. AD-6's "Validation code reading agent tokens" is prevented only for the Validation step.
*Proposed fix:* either move the mechanism decision into the v0.1 Workspace epic spine (Deferred row: "Decided in: v0.1 execution epic; default flips before general announcement"), or keep the row and add "v0.1 ships without enforcement; PRD departure, needs Owner sign-off" to Open Questions. In both cases name the allowlist's home (see P-3).

**P-5. NFR-15 free and open-source components, against AD-8 "ships those binaries". contradicted (needs a licence check).**
NFR-15: "Hephaestus depends only on free and open-source libraries and tools. Agents and their costs are the Owner's own." PRD §2.2: Hephaestus "requires agents the user already has access to". AD-8 makes each Hephaestus release ship the Agent CLI binaries. As far as this reviewer knows, Claude Code is distributed under Anthropic's commercial terms rather than an open-source licence (Codex CLI is open source); redistributing it inside an MIT-licensed image may not be permitted, and it turns an "Owner's own" Agent into a Hephaestus component. No input examined redistribution rights, and the memlog shows the question was not raised. The memlog does record that the vendor installer accepts a pinned version and publishes a signed manifest with checksums, so pinning does not require shipping.
*Proposed fix (AD-8):* "Each release pins one version of each Agent CLI and fetches it from the vendor's official channel, checksum-verified, into `hephaestus-agents` at install or upgrade; Hephaestus images contain no Agent binary." Add to Open Question 1: "Confirm each CLI's licence terms on redistribution." Add a convention row: "Dependencies: free and open-source licences only (NFR-15); a new dependency needs a licence check."

**P-6. FR-66 and every effect that is not Task-scoped. missing.**
The FR-66 note hands Story identity and drift to the architecture. The Deferred row defers it with a reason and a constraint ("must use the hidden marker and journal"), which is acceptable. But the constraint cannot be met with the conventions as written:
- The idempotency key is `<task-id>:<step-run-sequence>:<effect-kind>:<ordinal>` and `EFFECT` hangs only off `TASK` and `STEP_RUN`. Creating issues from Stories has no Task and no step run. The same holds for Project-level reports (FR-2, FR-61), the window summary Notification (FR-50), and Stack navigation updates (FR-39).
- "Running it again creates issues only for Stories that have none" needs a key derived from the Story, not from the run.
- AD-14 forbids branching on the Methodology name, so where Stories live and how they are read must come from the Methodology manifest. Nothing says so.
*Proposed fix:* generalise the key to `<scope-id>:<sequence>:<effect-kind>:<ordinal>` where scope is a Task or a Project operation, and add `PROJECT ||--o{ EFFECT`. Extend the Deferred row: "Story source is declared in the Methodology manifest; the created issue's marker carries a stable Story identity; nothing is written back to the Base Branch."

**P-7. Escalation from any agent step (FR-63, FR-65, FR-47, FR-48). missing.**
AD-13 lists result schemas per step kind, and the step diagram shows Escalation only out of Readiness. The PRD lets a BMAD workflow (FR-63), Planning (FR-65), and a Stack update (FR-47) end in "needs a decision", with a question that "names the decision needed and the options" (FR-48). Without a shared outcome, each step kind will invent its own way to say it, or an Agent's uncertainty becomes an `agent-error`.
*Proposed fix (AD-13):* "Every agent step result shares one envelope whose outcome is `completed` or `needs-owner-decision` (question, options). The second always becomes an Escalation."

**P-8. FR-25 authentication mode. missing.**
The spine never mentions API key versus subscription sign-in except inside Open Questions 3 and 4. The memlog carries it as an inherited constraint ("auth mode per agent is owner-chosen ... sign-in always through the vendor tool"); it did not land. It is not adapter-internal: the mode is an Installation Configuration attribute, it changes the invocation (see R-4), it decides whether NFR-5's spend limit is enforceable, and FR-25 contains a "never" (Hephaestus never presents a provider's sign-in itself).
*Proposed fix (AD-10 or a convention row "Agent credentials"):* "Each Agent has an auth mode, `api-key` or `subscription`, in Installation Configuration. The adapter chooses the invocation by mode. Hephaestus stores only credentials the Owner supplies and implements no sign-in flow."

**P-9. Three PRD "nevers" that are bound but not ruled. missing.**
All three are in the memlog's inherited constraints and in an AD's Binds list or the layer table, but no rule states them:
- **FR-14, FR-15.** AD-1 binds both; nothing says at most one Task is `Running` installation-wide, or that a Task with an unfinished step is dispatched before any `Queued` Task.
- **FR-32.** "Reviewer choice" appears only as a word in the Core row. The invariant is: the Reviewer is never the Agent of the latest Implementation, Correction, or Rework step run, and when the other Agent has no capacity the Task waits. It depends on AD-9's attribution rule, so it is not private to one feature.
- **FR-44.** AD-9 binds it through "accepts only the Task branch ref". The hard limits themselves (no write to the Base Branch except merging an approved pull request through the API; approval by a Trusted User on the head commit; no force-push or delete, ever) are not stated.
*Proposed fix:* one sentence each: AD-1 (single slot and resume-first), AD-13 (Reviewer is not the latest author; no override), AD-4 ("The gateway has no code path that pushes to, force-pushes, or deletes a ref outside `hephaestus/*`; the only write to the Base Branch is a merge through the API after Core has verified a Trusted User approval on the head commit").

**P-10. FR-4: a credential inside pushed content. missing.**
FR-4's first testable consequence is that a search of **repository contents** for any configured credential value finds nothing. AD-4 masks "every outbound text"; AD-16 masks text "before it is stored, logged, or sent out". A bundle is not text. An Agent, or repository code it runs, can write its own credential into a file; the checkpoint commits it and the gateway pushes it. A second gap: the registry is built from "every loaded secret value", so a credential refreshed during a run (Codex `auth.json`, Open Question 3) is not in it.
*Proposed fix (AD-9):* "Before pushing, the gateway scans the imported commits for registered secret values; a hit blocks the push, keeps the volume, and raises an Escalation." Add to Open Question 3: "a refreshed credential must enter the masking registry before any output of that run is stored."

### 1.2 Fine, but worth a line

| # | Requirement | Mark | Note and suggested line |
| --- | --- | --- | --- |
| P-11 | FR-2 "a Task already running keeps the settings it started with" | ok, worth a line | Landed in AD-16 (`CONFIG_SNAPSHOT`). Unstated: whether Alias resolution is part of the snapshot (an Alias edited mid-Task would change Model between steps), and whether a Task re-entering `Running` after `WaitingForOwner` or a Rework re-snapshots. Suggest: "The snapshot includes resolved Aliases. It is taken once per opt-in; a Rework takes a new one. Trusted Users and limits are always read live." |
| P-12 | FR-2, FR-7, FR-61 "no Task starts ... the Owner is told" | worth a line | No representation for "ready but held": invalid configuration, unknown Alias, duplicate Role Label, unknown Methodology. AD-1 requires every wait to be a state with a resume condition. Suggest a convention: a held Task stays `Queued` with a recorded hold reason projected to the issue; a Project-level hold is a persisted condition shown by the CLI and, from v0.2, notified. In v0.1 there is no Notification, so "the Owner is told" needs a named channel. |
| P-13 | FR-1 "history of past Tasks is retained" on Project removal | worth a line | `INSTALLATION ||--o{ PROJECT ||--o{ TASK` invites a cascading delete. Suggest: "Removing a Project deregisters it; no history row is deleted." |
| P-14 | FR-53 history survives Workspace removal | ok | Satisfied structurally: output lives on the data volume and history in SQLite, neither in the Task volume. `HANDOFF` and `ESCALATION` are not entities; say that they are derived from step runs and Signals, or add them. |
| P-15 | FR-34 "each round is recorded with what was returned to the Agent" | worth a line | Requires the context file set of each step run to be retained. Say whether it is structured history (permanent) or raw output (retention job). |
| P-16 | FR-30 attribution | ok, worth a line | Landed in AD-9 and the trailers convention. Unstated: the checkpoint commit is made even when nothing is left to commit, and each step run records the tip before and after, so "any commit" maps to a step without reading trailers (which a squash merge in v0.3 would lose). |
| P-17 | FR-35 draft pull request | ok, worth a line | Landed in both diagrams. Unstated: a Task that fails before any commit exists (Readiness, Planning) cannot open a pull request; say it fails with an issue comment only. Also say whether exhausted `timeout`, `agent-error`, and `unknown` end in the same draft pull request. |
| P-18 | FR-23 cancel, FR-6 withdraw, against AD-2 | worth a line | AD-2 makes Marker Label presence authoritative. After a CLI cancel the label is still present, so a level-triggered reconcile re-queues the Task, contradicting "does not start again unless a Trusted User opts it in again". Suggest: "Admission is triggered by the `labeled` event, not by label presence; an opt-in is a `labeled` event newer than the Task's last end." The diagram also lacks `WaitingForOwner` and `WaitingForCapacity` to `Cancelled`, and any exit from `Failed`. |
| P-19 | AD-2 against AD-15 for close events | worth a line | AD-2 lets "issue and pull request open or closed" override the Task; AD-15 says a non-Trusted User's Signal changes nothing. PRD: "Actions by anyone else are ignored." State that AD-2's human-intent facts are subject to AD-15. |
| P-20 | FR-60 safe upgrades | ok, worth a line | Landed in the Startup seed (copy, migrate, stop on failure). Missing lines: refuse to start on a database newer than the binary (the memlog's "no automatic downgrade" did not land); `hephaestus.yml` carries a schema version (also only in the memlog); a Task in flight across an upgrade continues with the new pinned CLI and templates, and the step run records which. |
| P-21 | NFR-13 retention | ok, worth a line | The Agent-output convention says "removed by a retention job" but drops the default (30 days) and the option to keep permanently. Add both; say that summaries and metrics inputs are structured history and are never removed. |
| P-22 | NFR-6 least privilege on GitHub | worth a line | AD-4 binds NFR-6 but states no permission set and does not say the gateway polls only registered Projects. The memlog's threat item (a push to a Task branch triggers the repository's Actions with its secrets; the App is not granted the `workflows` permission) did not land. Suggest a convention row "GitHub App permissions" listing the set per release, with `workflows` excluded and the consequence named (a Task that edits workflow files cannot be pushed and escalates). |
| P-23 | NFR-7 "recorded with its cause" | worth a line | The journal records intent and result. Add: "each intent references the Signal or step run that caused it." |
| P-24 | FR-51 Notification failure does not stop work | worth a line | AD-5 lists notification as a journaled effect, but its natural-key list has no lookup for one, so an intent without a result cannot be "resolved before anything is retried". Add: "Notifications are best effort and at most once; an unresolved intent is closed as abandoned and never blocks the loop." |
| P-25 | FR-16, FR-41, FR-50, SM-5: the work window | worth a line | "Window" (scheduled or on demand) is used by four features across three releases and by a metric, and is not an entity. Add `WORK_WINDOW` to the entity seed or a Deferred row. |
| P-26 | FR-55 and §8 metrics | worth a line | Metrics arrive in v0.2 but are "derived from actual executions only". State that v0.1 history records what SM-1 to SM-7 need, or early Tasks will be invisible to them. |
| P-27 | SM-1 "the Owner changing code by hand" against AD-9 | worth a line | AD-9 verifies a fast-forward of the last tip Hephaestus pushed. If the Owner pushes to the Task branch, the next push is rejected. Say what happens: fetch, treat as human intent, and escalate or incorporate. |
| P-28 | §5.5 Identity and Tone, NFR-17 English, FR-52 "what happened and what comes next" | worth a line (a convention row is missing) | No convention governs Owner-facing text. Suggest: "Owner-facing text: English, plain, states what happened and what comes next; forge references only in release names; rendered by Core templates, masked by the gateway." |
| P-29 | Glossary "Workspace: a container with its own checkout" | worth a line | The spine reshapes it into a volume per Task plus a container per step while the Vocabulary convention says Glossary terms are type names. Add: "`Workspace` = the Task volume and its step containers." |
| P-30 | FR-28 "inspects and validates the partial work before continuing" | ok | Template content under AD-12 and AD-14. No action. |
| P-31 | FR-37 to FR-40, FR-43 Stacks | ok (deferred) | The Deferred row covers update and merge mechanics. It does not name FR-40 (deciding whether Tasks above a failed Task can proceed, which may need an agent step kind and result schema under AD-13) or the Task-depends-on-Task relation missing from the entity seed. Add both to the row. |
| P-32 | §7.4 out-of-scope items | worth a line | Deferred names database engines and the dashboard only. Windows execution, other Task sources, distributed workers, and parallel execution are in the inputs as future directions with architectural seams; see A-1, I-2, I-3. |

### 1.3 PRD Open Questions

| OQ | Status in spine | Mark |
| --- | --- | --- |
| 1. Usage Limit signals | Deferred row, with reason, to the v0.2 epic spine after a spike | ok |
| 2. Rework Command form | Deferred row, with reason, to the v0.3 epic spine; must extend the Labels convention | ok (explicitly deferred) |
| 3. Independence when no dependency is declared | In the same Deferred row as label spellings, with the reason "label and command conventions inside one feature each" | worth a line: the reason does not fit. This is a policy, possibly an Agent judgement (FR-10 speaks of "automatic judgement"), which would add a result schema under AD-13. Give it its own row and reason. |
| 4. BMAD workflows unattended; issue-to-artifact mapping | First half is spine Open Question 2 and a Deferred row. Second half (how an issue that did not come from a Story maps to the spec or story file and sprint status BMAD works from) appears nowhere | silently dropped (second half); see A-8 |

---

## 2. Addendum (`addendum.md`)

| # | Item handed to the architecture | Status in spine | Mark |
| --- | --- | --- | --- |
| A-1 | Execution environment as an abstraction (image **and platform** per Project) so a Windows image can be added without touching the core | `IWorkspaceRuntime` exists; image is per Project (AD-8). Platform is not a field; Linux paths and a numeric user are fixed in a convention, and AD-12 has prompts pointing at `/hephaestus/context`. No Deferred row for Windows | silently dropped. Add a Deferred row: "Windows execution environments: out of scope (no CLI support found); the step container layout is owned by the runtime adapter, and Core and templates refer to locations by name, not by path." |
| A-2 | Multi-architecture Linux images | Deployment seed: linux/amd64 and linux/arm64 | ok |
| A-3 | Labels as opaque Aliases; role-prefixed labels | AD-10, Labels convention | ok |
| A-4 | `effort:<level>`, `priority:<level>` proposed, not confirmed | `effort:<alias>` fixed in the Labels convention; Priority spelling deferred | ok |
| A-5 | Notification channel candidates: SMTP, ntfy, GitHub mentions | Deferred row names ntfy and SMTP behind `INotifier` | ok (explicitly deferred). Worth a line: the third candidate, GitHub mentions, needs no infrastructure and would give v0.1 a way to tell the Owner something (see P-12). |
| A-6 | GitHub identity: App or machine user, "decide in architecture" | Decided as a GitHub App, but only in passing (AD-4 "the GitHub App credential", provider list) | ok, worth a line: say the App is registered by the Owner and that its private key is a secret on the secrets volume. Permission set: see P-22. |
| A-7 | Stacked branches: rework invalidates the stack, squash conflicts, retargeting, merge order: "risks to resolve in architecture" | Deferred row to the v0.3 epic spine, with reason, including the exception to fast-forward-only | ok (explicitly deferred) |
| A-8 | Mapping between an issue and BMAD's artifacts: "undecided" | Round 8 chose the Story bridge; the spine covers Story identity only | silently dropped for issues with no Story in a BMAD Project. It touches AD-12: context is read-only and outside the repository, while BMAD workflows expect artifact files inside it. Add to Open Question 2: "and what artifact a BMAD step works from when the issue has no Story." |
| A-9 | Configuration trust boundary: Base Branch only; trust and secrets on the installation side | AD-16 | ok |
| A-10 | Round 6 configuration split, enumerated | AD-16 states the split generically; the enumeration is absent | missing; see P-3 |
| A-11 | Residual risk: a merged change to Validation commands runs with Workspace privileges | Not in the spine; the threat model is a named companion | ok. Worth a line in Open Questions that the companion must carry it, with the open-egress exposure in P-4. |
| A-12 | Persistence: SQLite, "to be decided in an ADR" | Decided: paradigm, Stack, Deferred row for other engines | ok. No AD of its own, which is consistent with the altitude; `docs/adr/` is a named companion. |
| A-13 | Installation Configuration edited by CLI, possibly import and export | AD-16, AD-17; import and export not mentioned | ok (feature-internal) |
| A-14 | Versioning: strict SemVer, public surface | Public-surface convention | ok |
| A-15 | Retention: 30 days raw, structured permanent | Convention drops the number and the permanent option | worth a line; see P-21 |
| A-16 | Egress mechanism open (proxy, network policy, DNS) | Deferred | contradicted in timing against NFR-4; see P-4 |
| A-17 | Issue body snapshot (body or hash) | AD-15 | ok |
| A-18 | Spend limit depends on reported usage; say so when it cannot be enforced | Deferred row; AD-10 "declared as absent ... and reported" | ok |
| A-19 | Methodology as data, per lifecycle step | AD-14 | ok |
| A-20 | `--bare` skips skill discovery; a BMAD step in API-key mode needs the skill named and the directory added, or a non-bare run: "to verify" | Not mentioned; Open Question 2 does not include it and the spine never mentions bare mode | silently dropped. Add to Open Question 2: "and whether a BMAD step can run in bare mode with the skill named explicitly." Ties to P-8 and R-4. |
| A-21 | BMAD skills need `uv` and Python in the image; a Methodology may be "not available in the Project" (FR-61) | AD-8's image contract is "Linux, glibc or musl, `bash` and `git`"; Open Question 2 asks "what do they need in the image" | worth a line: "A Methodology manifest declares what the Project image and repository must provide; admission checks it (FR-61)." |
| A-22 | "v0.1 now depends on the BMAD spike. Schedule it first." | Open Question 1 makes the injection spike the "first story of v0.1"; the BMAD spike is "before the Methodology epic" | worth a line: two inputs each name a different "first". State the order. |
| A-23 | Story identity: written back into the Story or sprint file, or a mapping held by Hephaestus | Deferred; the "hidden marker and journal" constraint in effect selects a Hephaestus-side mapping | ok; make it explicit as in P-6 |
| A-24 | Developer machine is Windows 11 with Docker as host | Named volumes only (AD-6), `docker exec` CLI | worth a line: Open Question 1 tests three image families but not the Owner's actual host. Add Docker Desktop on Windows (WSL2) to the spike: socket mount, read-only volume mount, ownership of files in named volumes for user `10001`. |
| A-25 | Subscription sign-in is "policy risk, not a settled permission" | Absent | worth a line under P-8; the documentation duty (FR-25, NFR-8) belongs to the companions. |

---

## 3. Research (`research.md`)

### 3.1 Recommendations 1 to 11

| Rec | Summary | Status | Mark |
| --- | --- | --- | --- |
| 1 | CLIs behind one adapter per Agent; normalise events, results, session ids, failures | AD-10, AD-11. Session ids are deliberately unused (see 8) | adopted |
| 2 | No ACP now; keep the adapter narrow enough for an ACP-backed adapter later | The memlog records "no ACP, no Agent SDK"; the spine mentions neither. AD-10's shape (describe a one-shot invocation, parse one output stream, start no process) leaves no place for an ACP session, which is long-lived and bidirectional | silently dropped. Add a Deferred row: "ACP-backed adapters: not adopted (adapter-only, unstable); would need a session-capable port beside `IAgentAdapter`. Revisit with a third Agent." |
| 3 | Both auth modes; never implement sign-in; document the risk | Not in the spine | missing; see P-8 |
| 4 | Choose the invocation by auth mode: bare with an API key, non-bare with a subscription and the repository treated as untrusted executable content | Subscription half is in Open Question 4. The API-key half (bare) is absent, as is its interaction with BMAD skills (A-20) | silently dropped (half). Covered by the fix in P-8 plus one clause in Open Question 4: "API-key mode runs bare unless the Methodology needs repository skills." |
| 5 | Non-root user, permissions bypassed, limits enforced by the container | AD-6, AD-7, user `10001:10001` | adopted |
| 6 | Classify from layered signals; `/usage` probe before a Task; unclassifiable is unknown | "Unknown is never a Usage Limit" is in AD-11. Layered signals and the probe are deferred to v0.2 with a reason | adopted and deferred. The probe half matters in v0.1 for FR-26; see P-2. |
| 7 | Map Effort inside the adapter; an Alias resolves to Agent-specific parameters | AD-10 last sentence; Open Question 3 for Codex | adopted |
| 8 | Persist each Agent's session state per Task so a restarted container can resume | Rejected in AD-12 ("Session resume is not used"); `HOME` is discarded with the container. The memlog records it as a conscious departure with its cost | consciously departed. Worth a line in the spine itself, because the cost falls on two other rows: more tokens per round (earlier Usage Limits), and Codex subscription sign-in needs its refreshed credential written back (Open Question 3). |
| 9 | GitHub App; conditional polling; issue timeline for the labeller | AD-4, AD-15, provider list | adopted |
| 10 | Windows execution out of scope | Followed in fact; no Deferred row | adopted; see A-1 |
| 11 | Read the Symphony specification first; state differentiation as hypotheses | The memlog shows it was read and the reconcile-dispatch loop derives from it. The hypotheses belong to `docs/vision.md`, not the spine | adopted (outside the spine) |

### 3.2 Other research findings

| # | Finding | Status | Mark |
| --- | --- | --- | --- |
| R-12 | Finding 3 and cross-insight 1: in subscription mode the repository is executable configuration; "the container is the security boundary, not the agent" | AD-7 title and Open Question 4 | ok |
| R-13 | A run can end at the weekly limit with exit code 1 and no event | AD-11 maps it to `unknown`. Correct by the PRD, but then the Task neither hands off nor waits; it burns attempts and fails | worth a line for the v0.2 Deferred row: "an `unknown` failure with no output triggers an availability or usage probe before the next attempt." |
| R-14 | Open question 3: does the Codex sandbox work in a container | The memlog answers it (fails under Docker's default seccomp; run with the sandbox off). AD-7 states the rule generically | ok |
| R-15 | Open question 4: OpenAI terms on plan credentials in automation | Not in the spine | ok (documentation, not structure) |
| R-16 | Open question 5: does Codex report usage | Spine Open Question 3 | ok |
| R-17 | `--max-turns`, `--max-budget-usd` exist as run caps | AD-6: limits are set by the runtime, not requested from the Agent | consciously departed; fine |
| R-18 | Staleness: re-verify flags against installed versions when each adapter is started; earliest re-check 2026-12-25 | Tests convention tags transcripts with the CLI version; AD-8 pins versions | ok. Worth a line: the Stack rows for both CLIs are the stalest facts in the spine. |
| R-19 | Symphony comparison: "workspace lifecycle hooks, retry backoff, and reconciliation are solved problems there" | Reconciliation adopted. Retry backoff has no rule (see P-2). Lifecycle hooks: none; the Project image is the only preparation mechanism | worth a line: say that no per-Project setup hook exists in v1 and that preparation belongs in the image or the Validation commands. |

---

## 4. Original brief (`init.md`)

The PRD wins where they disagree. Items the PRD overrode (worktrees replaced by containers, locally installed Agents replaced by containerised ones, "never automatically merge" replaced by merge after approval, in-repository configuration) are not listed.

### 4.1 Section 23 D: ADR topics

| Topic | In spine | Mark |
| --- | --- | --- |
| Modular monolith | Paradigm, AD-3 | ok |
| Agent integration strategy | AD-8, AD-10, AD-11, AD-12 | ok; ACP and the Agent SDK rejections are unrecorded (rec 2) |
| Task provider abstraction | One port, `ICodeHost`, carries the task source (issues, labels, comments), the code host (push, pull requests, merge), and the mirrors | silently dropped: see I-2 |
| Local persistence | Paradigm, Stack, Deferred | ok |
| Workspace isolation | AD-6, AD-7, AD-9 | ok |
| Workflow execution | AD-1 and the fixed step kinds; matches the PRD non-goal | ok |
| Control plane and execution plane separation | Present in structure (Core loop against `IWorkspaceRuntime`; AD-10) but never named, and no Deferred row for distributed workers | worth a line: see I-3 |
| Security boundaries | AD-4, AD-7, AD-15, AD-16 | ok |

**I-2. Task provider abstraction. silently dropped.**
init.md §13 asks for "GitHub-first, not GitHub-only" and to "keep external provider-specific concepts outside the core domain where practical"; the PRD keeps other Task sources as a later addition (§7.4). The spine merges task source and code host into `ICodeHost`, and the Core vocabulary is GitHub's (labels, pull requests). That is defensible for v1 but it is a departure from a named ADR topic with no row.
*Proposed fix (Deferred row):* "Task sources other than GitHub issues: `ICodeHost` would split into a task source and a code host. Not in v1; Core names stay GitHub-shaped until then."

**I-3. Control plane and execution plane. fine-but-worth-a-line.**
*Proposed fix (Deferred row):* "Distributed workers and parallel Tasks: `IWorkspaceRuntime` and the bundle boundary (AD-9) are the seam; AD-1's single slot is the constraint to lift. After v1."

### 4.2 Section 22: engineering standards

| Standard | Spine | Mark |
| --- | --- | --- |
| Clear module boundaries, dependency inversion | AD-3 | ok |
| Explicit execution state | AD-1 | ok |
| Structured logging | Logging convention | ok |
| CancellationToken propagation, asynchronous I/O | Async convention | ok |
| Deterministic validation | AD-13 | ok |
| Idempotent operations | AD-5 | ok. Worth a line: Task state, the Signal inbox, and the effect journal sit behind three ports (`ITaskStore`, `ISignalInbox`, `IEffectJournal`), and the spine does not say that applying a Signal, changing state, and recording an intent commit in one transaction. Without it "intent before effect" is not guaranteed. |
| Integration tests | Tests convention (Testcontainers, real SQLite and Docker) | ok |
| Architecture Decision Records | Companion `docs/adr/` | ok |
| Avoid speculative abstractions; avoid generic repositories | Nine ports, none generic. `IOutputStore` and `ISecretStore` each have one plausible implementation in v1 | worth a line: they are forced by AD-3 (Core may not touch the file system or EF Core); say so in the companion so a contributor does not read them as speculative. |
| Avoid excessive project fragmentation | Six source projects and "one test project per source project, plus end-to-end" gives thirteen | fine. The memlog records the rejected alternative (one project with architecture tests). No contradiction, but the count is at the upper edge of what the brief tolerates. |
| Avoid large initial dependency graphs | Ten packages | ok |
| No provider-specific logic in the core | AD-3, AD-10, AD-14 | ok |

### 4.3 Other sections

| # | Brief item | Status | Mark |
| --- | --- | --- | --- |
| I-4 | §8 stack: OpenTelemetry | Deferred row with a reason | consciously departed |
| I-5 | §8 stack: .NET Aspire "where it provides measurable value"; "evaluate the proposed technologies and document any recommended changes" | The memlog says "no Aspire"; the spine is silent | worth a line in Deferred or the companion |
| I-6 | §8 stack: Worker Service, SignalR, React | Host is ASP.NET Core; dashboard deferred | ok |
| I-7 | §10 normalised Agent concepts: identity, capabilities, **health**, request, result, events, cancellation, failure classification, usage, session metadata | All present except health (see P-2) and session metadata (unused by decision, rec 8) | missing (health), via P-2 |
| I-8 | §16 principles: no Docker socket for Agents; do not disable Agent safety on the primary operating system; bounded retries; auditable history | AD-7, AD-1, AD-5 | ok |
| I-9 | §16 threat list (prompt injection through repository files, dependency installation, unauthorised network access) | Threat model is a companion; network access is P-4 | ok, subject to P-4 |
| I-10 | §17 Server Mode: authenticated remote access | Deferred row | ok |
| I-11 | §21 "must not automatically replace or deploy itself" | Nothing in the spine would do so; PRD non-goal | ok. Worth a line because Hephaestus will work on its own repository (SM-7) while holding the Docker socket: "Hephaestus never pulls, restarts, or replaces its own container." |
| I-12 | §2 English for code, comments, commit messages | Not stated; checkpoint commit messages and comments are generated by Hephaestus | worth a line; see P-28 |
| I-13 | §12 keep Role, Methodology, Agent, Model, Workflow, Execution environment independent | AD-10 (Alias resolution), AD-14 (template by step kind only), AD-8 (image per Project) | ok |

---

## 5. Places where the spine contradicts an input: summary

| # | Spine text | Input | Severity |
| --- | --- | --- | --- |
| C-1 | AD-11: "Only `usage-limit` may lead to Handoff or waiting for capacity." | FR-26: an unavailable Agent "is treated as having no capacity"; FR-28: Handoff when the Agent "cannot continue" | Real. One sentence fixes it (P-2). |
| C-2 | Deferred: egress allowlist mechanism "Before general announcement" | NFR-4: "In v0.1 access is open unless a list is set" | Real, as an unrecorded scope reduction (P-4). |
| C-3 | AD-8: each release "ships those binaries" | NFR-15: only free and open-source components; "Agents ... are the Owner's own" | Probable; depends on a licence check no input performed (P-5). |
| C-4 | Idempotency key and `EFFECT` are Task-scoped; AD-5 lists "issue creation" and Notifications as effects | FR-66, FR-50 summary, FR-2 Project-level reports: effects with no Task | Internal inconsistency exposed by the PRD (P-6). |
| C-5 | AD-2: Marker Label presence is authoritative and overrides the Task | FR-23: a cancelled Task "does not start again unless a Trusted User opts it in again" | Latent; resolved by making admission event-triggered (P-18). |

No contradiction was found with the research report or with any part of init.md that the PRD left standing.
