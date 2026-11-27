# Epic 2 Context: Opt issues in and converse on GitHub

<!-- Compiled from planning artifacts. Edit freely. Regenerate with compile-epic-context if planning docs change. -->

## Goal

A Trusted User applies the Marker Label to an issue and sees the Task's state on that issue. Role Labels, the Priority Label, and `Depends-On` lines are honored; labels and comments from anyone else are recorded and ignored. Hephaestus can ask a specific question on the issue, wait without stalling other Tasks, and resume the Task when a Trusted User answers. The epic builds the intake spine every later epic runs on: poller, Signal inbox, single-writer loop, admission, effect journal, and the state-label projection. It also adds the first live Dashboard view of Tasks.

## Stories

- Story 2.1: Notice what happens on registered Projects
- Story 2.2: Opt an issue in with the Marker Label
- Story 2.3: Withdraw, close, or opt in again
- Story 2.4: See a Task's state on its issue
- Story 2.5: Say on the issue what was ignored and why
- Story 2.6: Choose Agents and Effort per Task with Role Labels
- Story 2.7: Work Tasks in a predictable order
- Story 2.8: Declare that a Task depends on another
- Story 2.9: Ask the Owner a question and move on
- Story 2.10: Answer on the issue to resume the Task
- Story 2.11: Hold edited content until a Trusted User confirms it
- Story 2.12: See Tasks and their states in the Dashboard

## Requirements & Constraints

- An issue becomes a Task only when a Trusted User applies the Marker Label. Anyone else's attempt creates no Task and is recorded with the acting user's numeric id. Removing the label before work starts withdraws the Task.
- Role Labels pick the Primary Agent, Fallback Agent, and Effort per Task; a bare Marker Label uses the Project defaults. An unknown Alias, a duplicated role, or Primary and Fallback resolving to the same Agent keeps the Task `Queued`, passed over, with the problem reported on the issue. Fixing the labels makes it eligible without further action.
- Order of work: Priority Label first, then lower issue number. A Task with an unsatisfied `Depends-On` is passed over without holding others back. Only `Merged` satisfies a dependency, and only the admitted snapshot counts.
- Each Task's state shows on its issue as exactly one `hephaestus:<state>` label within one polling cycle, re-asserted if changed by hand.
- Escalation: a specific question comment naming the decision and the options; the Task moves to `WaitingForOwner`; other Tasks continue. A Trusted User's reply resumes the Task with the answer in context; anyone else's does not.
- Trust: an Agent sees only content a Trusted User vouched for. Edited content (detected by hash comparison) stays out until every editor is a Trusted User or one confirms with `/hephaestus confirm`. An edit whose editor cannot be established counts as untrusted.
- Ignored label actions by non-Trusted Users get one issue comment per user, action kind, and issue. Their comments and commands get no reply.
- Outside effects (labels, comments) are idempotent and audited with their cause; GitHub outages are retried within a bound without losing state; every retry and wait is bounded.
- Every sentence Hephaestus posts comes from `en`/`pl` resources in the Task's language. Labels, command syntax, and the hidden marker are never translated.
- Coverage stays at or above 80% overall and on new code. A security claim needs a passing test.

## Technical Decisions

- Poller (in `Hephaestus.GitHub`) only appends Signals and keeps cursors through `IPollCursorStore`. It decides no trust and changes no Task. Append first, advance the cursor second. Uses conditional requests, honors `304` and rate-limit waits, and has a configurable polling interval with a documented default. Each Signal carries source, source event id, and the actor's numeric id from GitHub's event data. A duplicate append is a no-op.
- The orchestrator loop is the only writer of Task state, through `IUnitOfWork`, which only the loop may depend on (architecture test). One transaction per Signal covers marking it applied, the Task writes, and the effect intents. Inbox order is a database sequence. Intent is read from actor-attributed events, never from the current label set. Startup recovery is the loop's first tick.
- One admission function in Core decides trust by numeric id. Hephaestus's own identity is never trusted and its content is never admitted. One parser in Core handles `/hephaestus <verb>` over a closed set; `confirm` only in v0.1. An unknown verb is reported and changes nothing.
- A Task is one Project plus one issue number for life. Opting in again reuses the Task and its branch. `Withdrawn` and `Cancelled` go back to `Queued`; `Closed` and `Merged` are final. The branch name `hephaestus/<issue-number>-<slug>` is derived once, with a slug of lower-case ASCII, digits, and hyphens, bounded in length.
- The issue title and body are snapshotted with their hash as vouched for by the Marker Label event. Context is built only from admitted snapshots. Core parses `Depends-On: #<n>` lines from the snapshot.
- Dispatch order, dependency satisfaction, and Alias resolution are Core policies computed from recorded state. No queue lives in memory. Aliases resolve in Core only.
- Effect journal: the loop writes it, append-only. Intent is committed with the state change, and the result is recorded afterwards. The key is `<scope>:<cause>:<effect-kind>:<ordinal>` and is passed to the port method as a parameter. Every comment ends with `<!-- hephaestus:effect=<key> -->`. Recovery resolves an unfinished intent by natural key: label as a set, comment by marker. A marker counts only if Hephaestus's own identity wrote it and it matches a journaled intent. An unreachable GitHub is retried at a recorded next-attempt time within a bound, then `WaitingForOwner`, not `Failed`. A permanent refusal is not retried: it is journaled with its reason, the Task moves to `WaitingForOwner`, and an error is logged with the Task id. In v0.1 such a Task is visible only in the log, and the documentation says so.
- Comment bodies are masked before they leave Hephaestus. Agent-written text posted in a comment has HTML comments escaped, mentions and issue-closing keywords broken, and command keywords fenced.
- Escalation goes through Core's single escalation path. It calls `INotifier`, whose v0.1 Host implementation does nothing, and the outcome is the same either way.
- Open items settled in this epic: the Priority Label spelling (added to the documented label conventions); what is reported when a dependency failed, was closed, is not a Task, or forms a cycle; opting in again after an untrusted merge; and when admitted content is re-read for the hash comparison. Also to be tested and documented: whether GraphQL `userContentEdits` returns editors to an installation token. If it does not, every detected edit is held.
- Labels: marker `hephaestus`, state `hephaestus:<state>` in kebab-case, roles `primary:<alias>`, `fallback:<alias>`, `effort:<alias>`. States are `queued`, `running`, `waiting-for-owner`, `waiting-for-capacity`, `pull-request-open`, `failed`, `cancelled`, `withdrawn`, `merged`, `closed`. A trusted issue close moves any non-final Task to `Closed`.
- The health endpoint reports that the loop is alive and when its last tick ran.
- Change stream: after each commit the loop (or a control API handler) announces through `IChangeFeed`. Events are `{scope, id}` only, with no content, and are not journaled. The stream is token-protected and bounded in open count, and revoking a token closes its streams. The Dashboard re-reads everything on reconnect. Only `Hephaestus.GitHub` talks to GitHub. The poller and gateway wait out rate limits.

## UX & Interaction Patterns

- Every comment Hephaestus posts has a fixed anatomy: a bold headline of what happened, at most five lines or one small table of facts, a "Next:" line where something follows, and the hidden marker last. Use Glossary terms exactly (Task, Validation, Finding, Escalation).
- An Escalation comment reads "Hephaestus needs a decision.", then the decision in one sentence, lettered options one per line, and how to answer. One Escalation asks for one decision.
- Dashboard (read-only; consult `EXPERIENCE.md` and `DESIGN.md` first):
  - The Tasks tab is a table of issue number, title, state chip, Primary and Fallback Alias, Effort, dependencies, last change, and an issue link. It is filtered by state, and the filter lives in the URL. A passed-over Task shows its reason under its title.
  - The state chip has a colour token, an icon, and the label's state name, with a tooltip saying what the state means and what ends it. It is not clickable.
  - Overview shows loop alive and last tick (or since when it has not ticked, pointing to the container log), a "Needs you" list of `waiting-for-owner` Tasks with the question's first line and a link to the issue, and per-state counts linking to the filtered list. There is no answer field.
  - Updates are live: rows update in place with no reload, no scroll jump, and no highlight beyond the chip changing. The indicator reads "Live" or "Reconnecting". Issue titles and bodies render as text, never markup.

## Cross-Story Dependencies

- 2.1 (poller) feeds 2.2 (loop and admission), which feeds 2.3 to 2.11. 2.4 introduces the effect journal, which 2.5, 2.6, 2.8, 2.9, and 2.11 reuse to post comments.
- 2.5 sets the rule for reporting ignored actions, and 2.6, 2.10, and 2.11 refer to it.
- 2.7 and 2.8 extend one dispatch-order policy. The policy is exercised at dispatch in Epic 3.
- 2.9 needs 2.4's retry bound, because an exhausted effect escalates through the same path. 2.10 and 2.11 build on 2.9, and 2.11 owns the `/hephaestus` parser and `confirm`.
- 2.12 needs the Dashboard seed from Epic 1 (1.14, 1.15) and the loop's commit announcements.
- Epic 3 relies on Escalation (blocked push, `needs-decision` result), on the dispatch policy, and on the loop. Epic 4 and later epics add Dashboard views to what 2.12 sets up. Epic 11 relaxes dependency satisfaction.
