---
name: Hephaestus
status: draft
sources:
  - ../../prds/prd-Hephaestus-2026-11-25/prd.md
  - ../../architecture/architecture-Hephaestus-2026-11-25/ARCHITECTURE-SPINE.md
  - ../../sprint-change-proposal-2026-11-25.md
  - ../../sprint-change-proposal-2026-11-25-language-and-ci.md
updated: 2026-11-25
---

# Hephaestus — Experience Spine

The Owner meets Hephaestus on three surfaces: the **Dashboard** in a browser, the **command line**, and the **text Hephaestus writes on GitHub**. This spine fixes how each behaves. `DESIGN.md` is the visual identity reference for the Dashboard; tokens are cited as `{path.to.token}`.

Drafted on the Fast path. Passages marked `[ASSUMPTION]` were written by the assistant and not chosen by the owner; they stand until the owner changes them.

## Foundation

- **Dashboard.** A single-page web application: Angular 22 with Angular Material (Material 3) and the CDK, served by Hephaestus on the host's loopback address (`http://127.0.0.1:8717`). Desktop first. It is read-only in v0.1 to v0.3: it has no control that changes a Task, a Project, or configuration. It stays current through one stream of change events and reads again what it shows (architecture AD-17).
- **Command line.** The `hephaestus` executable in client mode, run with `docker exec hephaestus hephaestus …`. It is where the Owner configures and controls Hephaestus, and it is part of the versioned public surface (FR-59).
- **GitHub.** Labels, issue comments, and pull request descriptions. It is where the Owner states intent, answers questions, and approves.

One rule divides the three: **GitHub is where work is asked for and approved, the command line is where the installation is run, and the Dashboard is where both are watched.** Whenever the Dashboard shows something the Owner would act on, it says where to act.

## Information Architecture

### Dashboard surfaces

| Surface | Route | Reached from | Purpose | From |
| --- | --- | --- | --- | --- |
| Unlock | `/unlock` | Any route without a valid token | Enter a Dashboard Token | v0.1 (Story 1.14) |
| Overview | `/` | Rail | Version; then whether the loop is alive and its last tick, Tasks that need the Owner, and counts of Tasks by state | v0.1 (version in 1.14; the rest in 2.12) |
| Projects | `/projects` | Rail | Registered Projects with Base Branch, protection, and the last configuration check | v0.1 (1.15) |
| Project | `/projects/:projectId` | Projects row | One Project. Tabs: **Tasks**, **Configuration** | v0.1 (1.15, 2.12) |
| Task | `/projects/:projectId/tasks/:issueNumber` | Task row, Overview | One Task: header, step timeline, Validation, then Findings, plan, and Correction rounds | v0.1 (3.22, 4.7) |
| Step output | Side sheet on Task, `?step=:stepRunId` | Timeline node | Stored, masked output of one step run or Validation command | v0.1 (3.22) |
| Installation | `/installation` | Rail | Trusted Users, Aliases, limits, protected paths, secret names, each Agent's sign-in mode and readiness | v0.1 (1.15; Agents from 3.22) |
| Project, **Schedule** tab | `/projects/:projectId?tab=schedule` | Project | Schedule with its time zone, open window, next window | v0.2 (6.4) |
| Agents | `/agents` | Rail | Capacity per Agent: ready, exhausted until, last probe; Tasks waiting for it | v0.2 (7.6) |
| Overview, status section | `/` | Rail | The running Task and its step, waiting Tasks with what they wait for, queued Tasks in dispatch order | v0.2 (9.6) |
| Statistics | `/statistics` | Rail | SM-1 to SM-7, SM-C1, SM-C2; each figure opens the Tasks behind it | v0.2 (9.6) |
| Pull requests | `/pull-requests` | Rail | Open pull requests with approval, coverage of the latest commit, and checks | v0.3 (10.4) |
| Project, **Stacks** tab | `/projects/:projectId?tab=stacks` | Project | Each Stack in order with state and position | v0.3 (11.8) |

`[ASSUMPTION]` The surface list, routes, and the split of views across tabs.

**Frame.** A navigation rail on the left lists the surfaces marked "Rail" that exist in the running release, in the order above. A top bar holds the page name, the live indicator, the language switch, the theme switch, and **Lock**, which forgets the token and returns to Unlock. There is no user menu: the Dashboard has one kind of user.

**Depth.** Three levels at most: list, item, side sheet. A side sheet never opens another.

**Every surface has an address.** Filters, the selected tab, and the open step live in the URL, so the back button works and a view can be bookmarked.

### What each v0.1 surface shows

| Surface | Content |
| --- | --- |
| Overview | Version. Loop: alive or not, last tick as relative and exact time. A list "Needs you": Tasks in `waiting-for-owner`, each with its question's first line and a link to the issue. A row of counts by state; each count links to the filtered Task list. |
| Projects | Table: name, Base Branch, protected or not, configuration valid or not with the commit it was read from, count of Tasks not in an end state. Removed Projects are listed last, marked removed. |
| Project, Tasks | Table: issue number, title, state chip, Primary and Fallback Alias, Effort, dependencies, last change. Filter by state. A Task passed over shows the reason under its title. |
| Project, Configuration | The settings of the last configuration read: Methodology, Validation commands, Correction bound, image, limits, allowed egress hosts, with the commit id. Invalid settings are listed with their reasons. |
| Task | Header: issue number and title, state chip, Agents and Effort, branch, links to the issue and the pull request. Step timeline. Under a Validation node: one row per command with exit status and duration. From Epic 4: Findings with id, class, and status; the plan; Correction rounds. |
| Installation | Five read-only lists. Secrets by name only. Agents: sign-in mode, ready or unavailable with the reason and the time of the last probe. |

## Voice and Tone

One voice on all three surfaces: a senior colleague reporting facts. Brand voice lives in `DESIGN.md`, Brand & Style.

| Do | Don't |
| --- | --- |
| Say what happened, then what comes next | Narrate effort ("working hard on…") |
| Use the Glossary's terms exactly, capitalised: Task, Validation, Finding, Escalation | Use synonyms: job, ticket, check, problem, question |
| Name the thing: "Validation failed: `dotnet test` exited with 1" | "Something went wrong" |
| "No data" for a value that does not exist | `0`, `-`, or `N/A` for a missing value |
| Give exact times with the zone; relative time only beside it | "a while ago" |
| Plain sentences, present or past tense | Exclamation marks, emoji, praise, apology |
| Say where to act when the reader must act | Leave a state unexplained |

**The forge.** At most one sentence per surface, only where nothing else is being said: an empty state or the release name. Never in a state name, an error, a question, a label, or a figure. `[ASSUMPTION]` Permitted lines: "The forge is cold. No Task is running." (Overview with nothing running) and the release name in the version line. Everything else is plain.

**Language.** English by default; Polish when the Owner chooses it (FR-69, architecture AD-21). One rule holds on all three surfaces: **what a person reads is translated; what a program reads never is.**

| Translated | Never translated |
| --- | --- |
| Dashboard labels, hints, empty states, tooltips, error sentences | State names, ids, commit ids, branch names, commands shown for copying |
| Command line sentences and table headers | Command names, options, `--json` output, exit codes |
| Comment headlines, the "Next:" marker, section titles of a pull request description, an Escalation's fixed sentences | Label names, `/hephaestus` verbs, `Depends-On:`, the hidden marker, commit trailers |
| Notifications | Configuration keys, log lines |
| Prose an Agent writes: summaries, Findings, plans, questions (in the Task's language) | Glossary terms: Task, Validation, Finding, Escalation, and the rest, in both languages |

Where the language comes from: the installation has one (`en` unless the Owner sets `pl`); a Project may name its own in `hephaestus.yml` for the text written on its issues and pull requests; the Dashboard starts in the installation's language and can be switched in the browser. The voice rules in the table above hold in both languages.

## Component Patterns

Behavioural. Visual specs live in `DESIGN.md`, Components.

| Component | Use | Behavioural rules |
| --- | --- | --- |
| State chip | Every place a Task state appears | Shows the state's name as in the `hephaestus:<state>` label. Its tooltip says what the state means and what ends it. Not clickable. |
| Task table | Project, Overview links | Sorted by dispatch order when filtered to `queued`, otherwise by last change, newest first. Paginated at 50 rows; no infinite scroll. A row is a link to the Task. |
| Step timeline | Task | Oldest step at the top. A node opens the Step output side sheet. A running step shows "Running since …" and no output link. A new step appears at the bottom without moving the reader's scroll position. |
| Output viewer | Step output | Text as stored, shown as text. Long output is loaded in pages of lines. A "Copy" action copies what is loaded. No search in v0.1. |
| Finding list | Task (Epic 4) | Blocking Findings first. Each shows id, class, status, and text; a Finding fixed in a later round stays listed as resolved. |
| Act-elsewhere hint | Wherever the Owner would act | One sentence and one target. For GitHub: a link that opens in a new tab. For the command line: the full command in `mono` with a copy button. The Dashboard never offers the action itself. |
| Live indicator | Top bar | "Live" while the change stream is open; "Reconnecting" while it is not. Its tooltip gives the time of the last successful read. |
| External link | Issue, pull request, commit, branch | Opens GitHub in a new tab, marked with an external-link icon. |
| Empty state | Any list with no rows | One sentence saying what would appear here, and an act-elsewhere hint when the Owner can cause it. |

**Where to act.** These hints are fixed:

| Seen in the Dashboard | Hint |
| --- | --- |
| Task in `waiting-for-owner` with a question | "Answer on the issue." Link to the issue. |
| Task held for an edit or for foreign commits | "Confirm on the issue with `/hephaestus confirm`." Link to the issue. |
| Task in `failed` | "To try again, remove and re-apply the `hephaestus` label." Link to the issue. |
| Project with an invalid configuration | "Fix `hephaestus.yml` on the Base Branch." Link to the file. |
| Project with eligible Tasks and no open window | The start command for that Project, copyable. |
| No Project registered | The command that registers a Project, copyable. |
| Pull request awaiting approval (v0.3) | "Approve on GitHub." Link to the pull request. |

## State Patterns

| State | Surface | Treatment |
| --- | --- | --- |
| No token | Any | Redirect to Unlock, keeping the address to return to. |
| Wrong token | Unlock | Inline under the field: "That token is not valid." The field keeps focus. Nothing says whether a token of that name exists. |
| Token revoked while in use | Any | The next read fails; the Dashboard forgets the token and shows Unlock with "This token was revoked. Issue a new one with the command below." and the command. |
| First load | Any | Material progress bar under the top bar and skeleton rows in the shape of the table. No spinner overlay. |
| Hephaestus not reachable | Any | The page keeps what it shows, dims it, and shows a banner: "Hephaestus is not answering. Showing what was read at 14:02:11." Retries on the change stream's schedule. |
| Change stream closed | Top bar | Live indicator shows "Reconnecting". Content stays. On reconnect every visible query runs again. |
| Loop not alive | Overview | The loop line turns to an error line: "The orchestrator loop has not ticked since …" with a hint to read the container log. |
| No Projects | Projects, Overview | Empty state with the register command. |
| No Tasks | Project, Tasks | "No Task yet. Apply the `hephaestus` label to an issue." |
| No step run yet | Task | "This Task has not started." If no window is open, the start command. |
| Step running | Task | Node shows "Running since …". Its output is not available until the step ends, and the node says so. |
| Output removed by retention | Step output | "This output was removed on … under the retention setting. The step's record is kept." |
| Value does not exist | Any | "No data" or "None", in `text-muted`. Never zero. |
| Unknown Task or Project in the address | Task, Project | "Nothing here." with a link to Projects. No stack trace, no id echo. |
| Project removed | Projects, Project | Marked "Removed on …". Its Tasks and history stay readable. |

## Interaction Primitives

- **Read, filter, follow links, copy.** These are the only interactions. The one form is Unlock.
- **The token** is typed or pasted into Unlock and sent in a request header. `[ASSUMPTION]` It is kept in the browser's session storage, so a reload does not ask again and closing the tab forgets it. It is never placed in an address. **Lock** forgets it at once.
- **Live by events.** A view changes when the change stream says its Task or Project changed. Rows update in place; a changed row is not highlighted or animated beyond the chip changing. The page never reloads and never moves the reader's scroll position.
- **Keyboard.** Standard browser and Material behaviour: Tab order follows reading order, Enter follows a link, Escape closes the side sheet. `[ASSUMPTION]` No custom shortcuts in v0.1.
- **Theme.** Follows the system; the switch in the top bar overrides it and the choice is remembered in that browser.
- **Language.** Starts in the installation's language; the switch in the top bar changes it between English and Polish and the choice is remembered in that browser. Switching requests nothing from another origin. Text from GitHub and from Agents is shown in whatever language it was written in.

**Banned everywhere:** a control that changes anything in Hephaestus; a field for a secret; content from GitHub or an Agent rendered as markup; anything loaded from another origin; a timer that refreshes the page; infinite scroll; a dialog on top of the side sheet; celebratory animation.

## Accessibility Floor

Behavioural. Contrast rules live in `DESIGN.md`, Colors.

- WCAG 2.2 AA.
- A Task state is always carried by icon and name as well as colour.
- Everything reachable by mouse is reachable by keyboard; the focus ring is always visible.
- On navigation, focus moves to the page's headline and the page name is announced.
- Live changes are announced politely and sparingly: a state change of the Task being viewed, and the stream reconnecting. Changes to rows in a list are not announced one by one.
- Tables have header cells and a caption naming what they list. Timestamps carry the exact time for assistive technology.
- Motion is limited to Material's defaults and is removed when the system asks for reduced motion.
- The output viewer is a text region that screen readers can read line by line; line numbers are not read.

## Responsive & Platform

The Dashboard is opened on the machine that runs Hephaestus, so a desktop or laptop browser is the target.

| Width | Behaviour |
| --- | --- |
| 1024 px and up | Navigation rail, full tables, side sheet beside the content. |
| 768 to 1023 px | Rail collapses to icons. Low-priority table columns (Effort, dependencies) fold into the row's second line. The side sheet covers the content. |
| Below 768 px | Usable, not designed for: the rail becomes a drawer and tables become stacked rows. The Owner's phone path is GitHub, not the Dashboard. |

Current versions of Chromium-based browsers, Firefox, and Safari. `[ASSUMPTION]`

## Command Line

The command line is a public surface. A change to a command, option, or the shape of `--json` output is a changelog entry (FR-59).

### Grammar

```
hephaestus <noun> <verb> [<target>] [--option <value>]
```

- **Noun, then verb.** Nouns are singular and hyphenated when two words. Three commands have no noun because they concern the installation as a whole: `version`, `health`, and, from v0.2, `status`.
- **Verbs come from a closed vocabulary:** `list`, `show`, `add`, `remove`, `set`, `clear`, `check`, `start`, `stop`, `cancel`, `issue`, `revoke`, `connect`, `test`, `history`, `output`, `create-issues`. A new command reuses one of these before a new verb is added.
- **Targets.** A Project is `<owner>/<repo>`. A Task is `<owner>/<repo>#<issue-number>`. Everything else is named by the name the Owner gave it.
- **Options** are long, `--kebab-case`, with a value. The only short option is `-h`. Every command has `--help`.
- **Secrets never appear in a command.** A command that takes a secret reads it from standard input and refuses it as an argument.
- **No prompts.** A command never asks a question; it runs under `docker exec`, often without a terminal. A command that removes something says what it removed.

### Command tree

`[ASSUMPTION]` The tree below was drafted from the stories. Each story may adjust its own commands within the grammar; a change to the grammar itself is a change to this spine.

| Command | Does | Story |
| --- | --- | --- |
| `version` | Prints the Hephaestus version | 1.4 |
| `health` | Loop alive, last tick | 2.2 |
| `secret set <name>` · `secret list` · `secret remove <name>` | Stores a value from standard input; lists names; removes | 1.5 |
| `github connect --app-id <id>` · `github check` | Connects the GitHub App (private key on standard input); reports identity, installations, and permissions | 1.7 |
| `trusted-user add <login>` · `list` · `remove <login>` | Manages Trusted Users | 1.8 |
| `alias add <name> --agent <agent> --model <model>` · `alias add <name> --effort <level>` · `alias list` · `alias remove <name>` | Manages Aliases | 1.9 |
| `limit set --task-duration <d> --cpus <n> --memory <size>` · `limit show` | Installation limits | 1.9 |
| `protected-path add <glob>` · `list` · `remove <glob>` | Protected paths | 1.9 |
| `setting list` · `setting set <name> <value>` | Installation settings with a default: `language` (`en` or `pl`), polling interval, step retries, effect retries, output retention, and later ones | 1.9 and after |
| `config show` | The whole Installation Configuration, secrets by name only | 1.9 |
| `project add <owner>/<repo> --base-branch <branch>` · `project list` · `project remove <owner>/<repo>` | Registers, lists, removes | 1.10 |
| `project check <owner>/<repo>` | Reads and validates `hephaestus.yml` from the Base Branch | 1.11 |
| `dashboard-token issue <name>` · `list` · `revoke <name>` | Issues a token and prints it once; lists names and creation times; revokes | 1.14 |
| `agent set-auth <agent> --mode <api-key\|subscription>` · `agent check <agent>` · `agent list` | Sign-in mode (credential on standard input); probe; list with readiness | 3.10 |
| `project start <owner>/<repo>` · `project stop <owner>/<repo>` | Opens and closes a window on demand | 3.14 |
| `signal show <id>` | Status of a Signal: pending, applied, or rejected with the reason | 3.14 |
| `task list <owner>/<repo>` · `task history <task>` · `task output <step-run-id>` | Tasks of a Project; a Task's step runs; stored output | 3.19 |
| `project create-issues <owner>/<repo>` | Creates issues from Stories | 5.3 |
| `schedule set <owner>/<repo> --days <list> --start <hh:mm> [--end <hh:mm>] --time-zone <zone>` · `schedule show` · `schedule clear` | Per-Project Schedule | 6.1 |
| `notification-channel add <name> --kind <email\|ntfy> …` · `list` · `remove` · `test` | Notification channels | 8.1 |
| `task cancel <task>` | Cancels a Task | 9.1 |
| `status` | Running Task and step, waiting and queued Tasks, windows, capacity | 9.2 |
| `metric list` · `metric show <id>` | Success metrics; one metric with the Tasks behind it | 9.3 |
| `limit set --spend <amount> --agent <agent> --period <period>` · `limit set --workspace-size <size>` | Spend and Workspace size limits | 9.4, 9.5 |

### Output

- **For people by default.** Lists are aligned tables with a header row; single items are `name: value` lines. Sentences and table headers follow the installation's language; values that are machine surface (states, ids, names) do not. No colour unless standard output is a terminal, and never when `NO_COLOR` is set.
- **`--json` on every command that prints data.** One JSON document on standard output, with camelCase keys and kebab-case enum values, in the same shape the API returns. Nothing else is written to standard output. It is identical in every language.
- **Absent is absent.** A missing value is `none` in a table and `null` in JSON. Never zero.
- **Times** are printed in UTC as ISO 8601. `[ASSUMPTION]`
- **Errors** go to standard error as the problem's title, then its detail. Exit code `0` for success, `1` for a failure the API reported, `2` for a command that was not understood.
- **Commands that send a Signal** print the Signal id. `[ASSUMPTION]` They then wait up to ten seconds and print whether it was applied or rejected; with `--no-wait` they return at once. A Signal still pending after the wait is reported as pending with the `signal show` command to follow it, and that is exit code `0`.
- **A value shown once** (a Dashboard Token) is printed alone on standard output, with its explanation on standard error, so it can be captured.

## Text on GitHub

Everything Hephaestus posts is composed by Core from recorded data (architecture AD-19). This section fixes the shape; the facts come from the Task's records.

### Comments

Every comment has the same anatomy:

1. **A headline** in bold: what happened, in one line.
2. **The facts**: at most five short lines or one small table.
3. **A line starting "Next:"** saying what Hephaestus will do or what it waits for.
4. The hidden marker as the last line.

The fixed words of a comment come from resources in the Task's language, or the Project's when no Task exists yet; the templates below are the English ones and each has a Polish counterpart. Longer material, such as a list of Findings, goes in a collapsed section under the facts. A comment never repeats what an earlier comment on the same issue already said as unchanged.

`[ASSUMPTION]` Templates. Text in angle brackets is filled from records.

| Event | Headline | Next |
| --- | --- | --- |
| Work starts | **Started.** `<Agent>` (`<Model>`, `<Effort>`) is implementing this Task on `<branch>`. | Next: Validation, then Review by `<other Agent>`. |
| Validation complete | **Validation passed.** or **Validation failed:** `<command>` exited with `<n>`. | Next: Review. / Next: Correction, round `<i>` of `<n>`. |
| Review complete | **Review complete.** `<b>` blocking and `<n>` non-blocking Findings from `<Agent>`. | Next: Correction, round `<i>` of `<n>`. / Next: the pull request. |
| Pull request opens | **Pull request opened:** `<link>`. | Next: your review. |
| Task fails | **Failed:** `<failure class>` in `<step>`. | Next: nothing until you opt this issue in again. Work so far is on `<branch>`. |
| Handoff (v0.2) | **Handed off.** `<Agent>` reached its Usage Limit; `<other Agent>` continues. | Next: `<step>`. |
| Waiting for capacity | **Waiting for capacity.** `<Agent>` is unavailable: `<reason>`. | Next: resumes by itself when `<Agent>` is ready`<, expected at time>`. |
| Label action ignored | **Ignored.** `<login>` is not a Trusted User, so the `<label>` label has no effect. | (none) |
| Held for confirmation | **Held.** `<what>` was changed by someone who is not a Trusted User. | Next: a Trusted User confirms with `/hephaestus confirm`. |

### Escalations

An Escalation is a comment with a fixed shape, so that it can be answered from a phone:

1. Headline: **Hephaestus needs a decision.**
2. The decision, in one sentence.
3. The options Hephaestus sees, lettered, one line each.
4. "Reply on this issue. Any reply from a Trusted User returns the Task to the Queue with the answer."
5. The hidden marker.

One Escalation asks for one decision. Agent-written question text is neutralised before posting and is quoted, not paraphrased. The fixed sentences are in the Task's language, and the Agent is asked to write its question in the same one.

### Pull request description

Sections, in this order; a section with nothing to say is left out.

| Section | Content |
| --- | --- |
| **What changed** | The implementation summary. |
| **Task** | A link to the issue. |
| **How it was checked** | A table: Validation command, result, duration. Then the Reviewer with Model and Effort and the count of Findings. |
| **Who did what** | Implementer with Model and Effort; Correction rounds if any. |
| **Findings** | Non-blocking Findings as a list. In a draft pull request: the blocking Findings still open and what still fails, first. |
| **Plan** | The plan, collapsed, when the Task was planned. |
| **Stack** (v0.3) | Position in the Stack, links to the previous and next pull request, stacked or independent and why. |

A draft pull request for a failed Task opens with one line before the sections: **This Task did not pass.** followed by what was attempted.

`[ASSUMPTION]` The **Task** section names the issue by its number alone (`Issue #7`) and never with a closing keyword, so GitHub links the pull request and the issue and merging the pull request does not close the issue. Settled in Story 3.13 without the Owner's answer, for the safer default: a closing keyword would close the issue on a merge into the default branch and not on a merge into another Task's branch, so the same Task would behave differently by where its pull request is merged; and Hephaestus itself never closes an issue, which stays the Owner's. To change it, say so on the pull request of Story 3.13: the keyword is one line in `PullRequestDescription` and one test.

### Labels

State labels are `hephaestus:<state>`, as the architecture fixes them. The Dashboard's state chip uses the same names.

## Key Flows

`[ASSUMPTION]` These flows were written by the assistant from the PRD's journeys; the owner did not narrate them. The protagonist is Adam, as in the PRD.

### Flow 1 — First look (Adam, the evening he installs Hephaestus)

1. Adam has started the container and registered a Project from the command line.
2. He runs `dashboard-token issue laptop`. The token is printed once.
3. He opens `http://127.0.0.1:8717`. The Dashboard shows Unlock. He pastes the token.
4. Overview loads: the version, "Loop alive, last tick 3 seconds ago", and no Task yet.
5. He opens Projects. His repository is listed: Base Branch `main`, protected, configuration valid at commit `a41c…`.
6. **Climax:** in another tab he applies the `hephaestus` label to an issue. Without touching the Dashboard he sees the Project's count change to one and, on the Tasks tab, the issue appear with a `queued` chip. Nothing he did in the Dashboard caused it, and he can see that it knows.

Failure: he pastes the token with a missing character. Unlock says "That token is not valid." and keeps the field focused.

### Flow 2 — What happened today (Adam, 22:00, after a day's window)

1. Adam opens the Dashboard. The token is still held by the tab; Overview loads.
2. "Needs you" lists one Task with the first line of its question. The counts show four `pull-request-open` and one `failed`.
3. He opens the failed Task. The timeline reads top to bottom: Readiness, Implementation, Validation failed, Correction, Validation failed, Correction, Validation failed.
4. He opens the last Validation node. The side sheet shows `dotnet test` exited with 1 after 48 seconds, and the stored output.
5. **Climax:** in the output he reads the one test that fails every round and recognises a fixture that needs a service the image does not have. The Task's header links the draft pull request and the hint says how to try again. He knows what is wrong and where to fix it without opening a terminal.

Failure: the output is older than the retention period. The side sheet says it was removed and that the step's record is kept; the exit status and duration are still there.

### Flow 3 — Answering a question (Adam, on his phone, then back at his desk)

1. A Notification says a Task needs a decision. On his phone, Adam opens the issue on GitHub.
2. The Escalation reads: "Hephaestus needs a decision." One sentence, options A and B.
3. He replies "B".
4. **Climax:** back at his desk an hour later, the Dashboard he left open shows that Task as `running`, with a new Readiness node at the bottom of its timeline. He did nothing in the Dashboard, and it is already past the question.

Failure: he replies from an account that is not a Trusted User. Nothing changes and nothing is posted in reply; the Dashboard still shows `waiting-for-owner` with its hint.
