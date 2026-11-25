---
title: Sprint Change Proposal — dashboard seed in v0.1, Angular
project: Hephaestus
date: 2026-11-25
status: approved
approved_by: Adam
approved_on: 2026-11-25
scope: major
mode: batch
---

# Sprint Change Proposal — a dashboard from v0.1, built with Angular

## 1. Issue summary

**Trigger.** Raised by the Owner on 2026-11-25, directly after sprint planning and before any story started. No story revealed it; the readiness gate did, by pointing at the empty UX documents.

**Change requested.**

1. Hephaestus gets a web dashboard starting in v0.1, as a seed, instead of the dashboard being out of scope for v0.1 to v0.3.
2. The dashboard grows with each epic, where that epic gives the Owner something new to see.
3. The front end is built with Angular, not React.

**Kind of change.** A new requirement from the Owner that reverses a recorded decision. The spec session parked the dashboard ("stays a non-goal for v0.1 to v0.3", Angular named as a candidate, options: v0.4 or a read-only view in v0.2).

**Decisions the Owner made for this proposal (2026-11-25).**

| Question | Decision |
| --- | --- |
| What the v0.1 seed does | Read-only. Projects, Tasks with their state, step history, Validation results. Nothing in the dashboard changes state; actions stay in the CLI and on GitHub. |
| How a browser reaches it | The Hephaestus process serves the dashboard on a second, TCP listener published only on the host's loopback address, behind a token issued from the CLI. Remote access stays deferred to Server Mode. |
| How the Dashboard stays current | Server-Sent Events: a one-way stream from Hephaestus that says what changed. SignalR was considered and set aside, because its browser transport puts the token in the address and its channel is two-way. |
| How to review this proposal | All changes at once. |

**Evidence that the plan conflicts with the request.**

- `prd.md` §7.4: "Dashboard, including the statistics page" is out of scope for all three releases.
- `SPEC.md`, Non-goals: "Out of scope for v0.1 to v0.3: a dashboard and statistics page".
- FR-55: "A statistics page is out of scope."
- Spine AD-17: "a local HTTP API on a Unix domain socket inside that container … The API is not exposed outside the container in v1."
- Spine Deferred: "Dashboard — After v1" and "API authentication and remote access — Server Mode".
- Threat model T12: "The API listens on a Unix domain socket inside the orchestrator container and is on no network."
- Story 1.4: "it publishes no port and the API listens on no network address".
- `init.md` §8 and §18 name React for the dashboard; no planning artifact after it names a front-end technology.
- `DESIGN.md` and `EXPERIENCE.md` are empty drafts.

## 2. Impact analysis

### 2.1 Checklist record

| Item | Status | Finding |
| --- | --- | --- |
| 1.1 Triggering story | N/A | No story started. |
| 1.2 Core problem | Done | New requirement; reverses a parked decision. |
| 1.3 Evidence | Done | Section 1. |
| 2.1 Current epic | Done | Epic 1 can be completed; it gains two stories and one changed criterion. |
| 2.2 Epic-level changes | Done | No epic added, removed, or redefined. Seven epics gain one or two stories. |
| 2.3 Remaining epics | Done | Epics 5, 8, and 12 are unaffected. |
| 2.4 Obsolete or new epics | Done | None obsolete. No new epic: the Owner wants the dashboard to grow inside each epic. |
| 2.5 Order and priority | Done | Epic order unchanged. The proof-of-concept path is unchanged; no dashboard story is on it. |
| 3.1 PRD | Action needed | Two new requirements, FR-55 amended, §7 release scope and out-of-scope list changed. |
| 3.2 Architecture | Action needed | AD-17 amended, Stack, source tree, deployment, Deferred, capability map. |
| 3.3 UX | Action needed | No UX contract exists. One is now required. |
| 3.4 Other artifacts | Action needed | Threat model, `Dockerfile`, `compose.yaml`, CI, license check, installation documentation. |
| 4.1 Direct adjustment | Viable | Effort medium, risk medium. |
| 4.2 Rollback | Not viable | Nothing is built. |
| 4.3 MVP review | Viable in part | v0.1 scope grows; nothing is cut. |
| 4.4 Selected path | Done | Direct adjustment with a deliberate growth of v0.1 scope. |
| 5.1 to 5.5 | Done | This document. |
| 6.3 Owner approval | Done | Approved by the Owner on 2026-11-25, with Server-Sent Events chosen for live updates. |
| 6.4 `sprint-status.yaml` | Done | Regenerated on 2026-11-25 from the updated `epics.md`: 12 epics, 92 stories, ten new keys in `backlog`, nothing dropped. |

### 2.2 Epic and story impact

| Epic | Change | Stories |
| --- | --- | --- |
| 1 Install and register | One criterion changed, two stories added | 1.4 changed; new 1.14, 1.15 |
| 2 Intake | One story added | new 2.12 |
| 3 Issue to pull request | One story added, one test extended | new 3.22; 3.2 extended |
| 4 Review and correction | One story added | new 4.7 |
| 5 BMAD Method | None. The Methodology and template of each step are already part of step history. | |
| 6 Schedules | One story added | new 6.4 |
| 7 Usage Limits | One story added | new 7.6 |
| 8 Notifications | None. Channels are configured from the CLI and there is nothing to follow. | |
| 9 Control and insight | One story added; it delivers the statistics page | new 9.6 |
| 10 Merge and rework | One story added | new 10.4 |
| 11 Stacks | One story added | new 11.8 |
| 12 Authoring aid | None. Repository assets only. | |

Totals: 82 stories become 92. v0.1 grows from 54 to 59 stories. The proof-of-concept path stays at 30.

Stories 1.1, 1.2, and 1.3 are untouched and can start before the documents below are updated.

### 2.3 Technical impact

- **A second listener in the process that holds the Docker socket.** That process is equivalent to root on the host. Today nothing on any network can talk to it. After this change a local browser can, which is the reason for the dashboard and the main risk of it.
- **A second toolchain.** Node.js and the Angular CLI join the build. They stay out of the runtime image: the dashboard is built in a build stage and copied in as static files.
- **One addition to the orchestrator loop.** After each committed transaction it announces, inside the process, which Tasks and Projects changed. The announcement is not an effect and is not journaled: losing one costs nothing, because the Dashboard reads again when it reconnects.
- **No change** to the Signal model, the effect journal, persistence, the workspace runtime, the gateway, or either adapter. The dashboard reads; it appends no Signal.

## 3. Recommended approach

**Direct adjustment, with v0.1 scope grown on purpose.** Add the dashboard as a read-only client of the existing API, one story per epic that has something to show, and amend the one architecture decision that forbids it.

**Why this path.**

- The architecture already expects it: AD-17 binds "future dashboard" and Deferred says it "will be a client of the same API". The change is when and how it is reached, not what it is.
- Read-only keeps the hard rules intact. Every state change still goes through a Signal from the CLI or from GitHub; secrets are still entered on standard input only (AD-16); the single writer stays single (AD-1).
- Growing story by story means each view is built when its data exists, and no dashboard work blocks the proof of concept.

**What it costs.**

| | |
| --- | --- |
| Effort | Medium. Ten stories, five of them in v0.1. A UX pass that did not exist. Four planning documents to update. |
| Risk | Medium. The risk is the listener, not the Angular code. It is bounded by three rules in section 4.2: loopback only, token required, read-only by construction. |
| Timeline | The proof-of-concept milestone is not delayed. v0.1 as a release grows by five stories. |

**Alternatives set aside.**

- *A dedicated dashboard epic.* Rejected by the Owner's direction: the dashboard should grow with each epic.
- *A separate dashboard container.* Keeps browser traffic out of the root-equivalent process, at the price of another component. The Owner chose the single process. It remains available later without changing the API.
- *Dashboard in v0.2 or v0.4.* The two options recorded when the dashboard was parked. Superseded by this request.

## 4. Detailed change proposals

Final wording belongs to the skill that owns each document (section 5). Requirement and decision ids below are proposed.

### 4.1 PRD (`prd.md`)

**§3 Glossary — add**

> **Dashboard** — The web interface through which the Owner follows Hephaestus in a browser. It shows; it changes nothing.
>
> **Dashboard Token** — A credential the Owner issues and revokes from the command line. A browser must present it to read anything from the Dashboard.

**§4 — new capability "Dashboard" with two requirements**

> **FR-67: Read-only Dashboard** [v0.1; grows with each release]
>
> The Owner can open a Dashboard in a browser and follow Hephaestus there. In v0.1 it shows the registered Projects, the Installation Configuration without any secret value, each Project's Tasks with their current state, and each Task's history. Each release adds views for what that release introduces.
>
> Consequences (testable):
> - No view and no request the Dashboard makes changes a Task, a Project, or any configuration.
> - Everything shown is read from the same records the command line reads; the two never disagree.
> - No secret value is shown. Agent output is shown as stored, that is, masked.
> - Text that came from GitHub or from an Agent is shown as text and is never interpreted as markup or script.
> - A value that does not exist is shown as none, not as zero.
> - A change to what is on screen appears without the Owner reloading the page.
> - The Dashboard works with no connection to any host other than Hephaestus itself.

> **FR-68: Dashboard access** [v0.1]
>
> The Dashboard can be reached only from the machine that runs Hephaestus, and only with a Dashboard Token. The Owner issues, lists, and revokes tokens from the command line.
>
> Consequences (testable):
> - A request without a valid token returns no data.
> - A revoked token stops working at once.
> - The Dashboard is not reachable from another machine, from a Workspace, or through the egress proxy.
> - The token value is shown once, when issued, and is not stored in readable form.
>
> Out of scope: access from other machines, encryption in transit, and more than one user. These arrive with Server Mode.

**FR-55 — amend**

OLD:
> Hephaestus derives the metrics in §8 from actual executions only and shows them to the Owner from the command line. … **Out of Scope:** a statistics page; it arrives with the dashboard.

NEW:
> Hephaestus derives the metrics in §8 from actual executions only and shows them to the Owner from the command line and on the Dashboard's statistics page. *(The "Out of Scope" line is removed.)*

**§7.1 v0.1 — add a line**

> - Dashboard, read-only seed: FR-67, FR-68.

**§7.2 and §7.3 — add a line each**

> - v0.2: the Dashboard gains Schedules and windows, Agent capacity and Handoffs, status, and the statistics page (FR-67, FR-54, FR-55).
> - v0.3: the Dashboard gains pull requests awaiting approval and Stacks (FR-67).

**§7.4 Out of scope — replace one line**

OLD:
> - Dashboard, including the statistics page.

NEW:
> - Actions from the Dashboard: it stays read-only in all three releases.
> - Dashboard access from another machine, and more than one user.

### 4.2 Architecture spine (`ARCHITECTURE-SPINE.md`)

**AD-17 — amend**

OLD:
> **AD-17 — One host process; the CLI is an API client.** … The API is not exposed outside the container in v1.

NEW (rule text to add; the existing rule about the CLI and Signals stays):
> **AD-17 — One host process; the CLI and the Dashboard are API clients.**
>
> The host process has two listeners.
>
> - The **control listener** is the Unix domain socket inside the container. It carries the whole API and is the only place an endpoint that appends a Signal, changes configuration, or touches a secret is mapped.
> - The **dashboard listener** is TCP. It serves the Dashboard's static files and read endpoints only. No endpoint that is not a read is mapped on it; an architecture test fails when one is.
>
> The dashboard listener is published on the host's loopback address only, and the `hephaestus` container is never attached to the workspace network. It checks the request's host name against the loopback names it was configured with and refuses any other. Every read endpoint requires a Dashboard Token, sent in a request header; the Dashboard uses no cookie. Tokens are created and revoked through the control listener, stored only as a hash, and never logged.
>
> The Dashboard is a single-page application built at image build time and served as static files. It loads nothing from any other origin, and the listener sends a content security policy that allows none. It renders text from GitHub and from Agents as text.
>
> The Dashboard learns of changes from one **change stream**: a read endpoint on the dashboard listener that answers with Server-Sent Events. After each committed transaction the loop announces which Tasks and Projects changed through a Core port implemented in Host; the stream forwards that as an event carrying ids and the kind of change, never content. The Dashboard then reads what it shows through the ordinary read endpoints, and reads everything on screen again whenever the stream reconnects. The announcement is not an effect and is not journaled. The stream requires the token in a request header like every other read, so the Dashboard opens it with a streaming request, not with the browser's `EventSource`. The number of open streams is bounded. No channel on which a browser can send exists; Agent output is not streamed while a step runs.
>
> **Prevents (added):** a browser, a web page the Owner visits, or a stolen token changing anything; the root-equivalent process answering a request from a Workspace or from another machine; a view that is silently stale; a second, two-way protocol beside the API.

**Stack — add rows** (exact versions pinned by the architecture update, as for every other row)

| Name | Version |
| --- | --- |
| Angular | current stable major at authoring |
| TypeScript | the version that Angular release requires |
| Node.js (build stage only; not in the runtime image) | current LTS at authoring |

Server-Sent Events need no row: they are part of ASP.NET Core 10 and of the browser.

Deliberately absent, added to the existing list: a second web server, a component served from a content delivery network, SignalR or any other two-way channel.

**Source tree — add**

```
  src/
    Hephaestus.Dashboard/     # Angular workspace; built into static files served by Hephaestus.Host
```

`Hephaestus.Host` gains the dashboard listener and the read endpoints. `Hephaestus.Dashboard` references no .NET project and is referenced by none; its only contract with the rest is the read API.

**Deployment — amend**

- `Dockerfile` gains a Node.js build stage; the runtime image contains the built files and no Node.js.
- `compose.yaml` publishes the dashboard port on `127.0.0.1` only.
- GitHub Actions builds and tests the Angular workspace on every pull request; the license check of Story 1.3 covers its packages.

**Deferred — amend two rows**

OLD:
> | API authentication and remote access | The API is unreachable outside the container in v1 (AD-17). | Server Mode |
> | Dashboard | Out of scope for v1; it will be a client of the same API. | After v1 |

NEW:
> | Remote access to the Dashboard, encryption in transit, more than one user | In v1 the dashboard listener is on loopback only, behind a token (AD-17). | Server Mode |
> | Actions from the Dashboard | Read-only in v1. An action would be a Signal like any other (AD-17), with protection against cross-site requests added first. | After v1 |

**Consistency conventions — add**

> | Public surface | *(add)* The Dashboard and the HTTP API are not part of the versioned public surface in v1: both ship in the same image and always match. |

**Capability map — add a row** for the Dashboard: lives in Host and Dashboard; governed by AD-16, AD-17.

`docs/architecture.md`, `docs/architecture-c4.md`, and ADR-0015 are rendered from the spine and are rendered again.

### 4.3 Threat model (`docs/security/threat-model.md`)

**T12 — amend**

OLD:
> | T12 | Reaching the Hephaestus API from a step container. | The API listens on a Unix domain socket inside the orchestrator container and is on no network (AD-17). | Low. |

NEW:
> | T12 | Reaching the Hephaestus API from a step container. | The control listener is a Unix domain socket inside the orchestrator container. The dashboard listener is TCP, but the orchestrator container is not on the workspace network and the egress proxy refuses private and loopback destinations (AD-7, AD-17). | Low. To be verified by test, directly and through the proxy. |

**New threats**

| # | Threat | Mitigation | Residual |
| --- | --- | --- | --- |
| T34 | **Another process or user on the host reads the Dashboard** through the loopback port. | Every read needs a Dashboard Token; tokens are stored hashed, shown once, and revocable (AD-17). | Low to medium. Loopback is shared by everything on the host; the token is the only barrier. |
| T35 | **A web page the Owner visits attacks the Dashboard**: a cross-site request, or a host name rebound to the loopback address. | The listener serves reads only, uses no cookie, and refuses host names other than its own (AD-17). | Low. |
| T36 | **Script injected through content shown in the Dashboard**: an issue body, a comment, a Finding, or Agent output. | Such text is rendered as text; the content security policy allows no inline script and no other origin (AD-17). | Low. Depends on no view bypassing the framework's escaping; an automated check forbids it. |
| T37 | **A stolen Dashboard Token.** | It reads; it cannot change anything, because no changing endpoint exists on that listener. It shows no secret value. It is revocable. | Medium for confidentiality: the holder reads Task history and masked Agent output. |
| T38 | **Exhausting the orchestrator with open change streams.** | A stream needs a valid token, the number of open streams is bounded, and a stream carries ids only (AD-17). | Low. |

**§7 Known limitations — add**

> The process that holds the Docker socket now answers HTTP requests on the host's loopback address. A flaw in that listener is a flaw in a root-equivalent process.

**§8 Verification plan — add** the tests named in Story 1.14 below.

### 4.4 Spec (`SPEC.md` and companions)

Re-derived from the PRD and the spine after 4.1 and 4.2:

- `SPEC.md`: new **CAP-15 — Dashboard** (FR-67, FR-68). Non-goals line "Out of scope for v0.1 to v0.3: a dashboard and statistics page; …" loses its first item and gains "actions from the Dashboard; Dashboard access from another machine". One constraint added: the Dashboard is read-only and reachable only on loopback with a token. The assumption that the UX documents do not exist is replaced by a reference to them as companions.
- `requirements.md`: CAP-15 added; FR-55 amended.
- `release-scope.md`: the three additions of 4.1.
- `glossary.md`: Dashboard, Dashboard Token.
- `success-metrics.md`: "shows each measure from the command line" gains "and on the Dashboard".

### 4.5 UX (`DESIGN.md`, `EXPERIENCE.md`)

Both are empty and are now required. One pass with `bmad-ux` covers three surfaces:

1. **The Dashboard.** Information architecture, the views of v0.1 and where later views attach, empty and error states, how a Task's state and history are read at a glance, and whether a component library is used (it must be free and open-source, NFR-15).
2. **The command line.** Stories name commands only by what they do ("the command that checks the Project"). The command tree, option style, and output conventions are fixed here, because the CLI is part of the versioned public surface (FR-59) and Story 1.4 is the first story to add a command.
3. **Text on GitHub.** The shape of progress comments, Escalations, and the pull request description (FR-36, FR-48, FR-52).

Identity and tone follow PRD §5.5: professional, with restrained references to the forge.

Items 2 and 3 are not caused by this change. The readiness gate found them undesigned; they are included because the same pass settles them.

### 4.6 Epics and stories (`epics.md`)

**Requirements inventory**

- Add FR-67 and FR-68 with their text. Amend FR-55. Amend AR-26 to the new AD-17.
- Add AR-72 (Dashboard build: Angular workspace, static files, Node.js in the build stage only), AR-73 (Dashboard security tests, threat model T12 and T34 to T38), and AR-74 (change stream: Server-Sent Events, ids only, announced by the loop after commit, not journaled, bounded).
- Replace the section "UX Design Requirements — None" with the requirements the UX pass produces.
- FR Coverage Map: FR-67 to Epic 1 (extended in Epics 2, 3, 4, 6, 7, 9, 10, 11); FR-68 to Epic 1.
- Proof-of-concept path: add a note that no dashboard story is on it.

**Story 1.4 — change one criterion**

OLD:
> **Given** the running container
> **When** it is inspected
> **Then** it publishes no port and the API listens on no network address

NEW:
> **Given** the running container
> **When** it is inspected
> **Then** the control API listens on a Unix domain socket and on no network address
> **And** the container publishes no port other than the dashboard port that Story 1.14 adds, bound to the host's loopback address

Rationale: the criterion would otherwise be broken by Story 1.14.

**Story 3.2 — extend one criterion**

The criterion "a process inside a step container … tries to reach the Docker socket, the Hephaestus API socket, or any host path" gains "or the dashboard port, directly and through the egress proxy".

**New Story 1.14: Open the Dashboard on my own machine**

As the Owner, I want to open Hephaestus in a browser on the machine that runs it, with a token only I hold, so that I can look at the forge without it becoming reachable by anyone else.

- **Given** a running installation **When** the Owner issues a Dashboard Token from the command line **Then** its value is printed once, only its hash is stored, and it is listed by name and creation time (FR-68)
- **Given** a valid token **When** the Owner opens the dashboard address on the host and supplies it **Then** the Dashboard loads and shows the Hephaestus version, whether the loop is alive, and when its last tick ran
- **Given** no token, a wrong token, or a revoked token **When** any read endpoint is requested **Then** no data is returned
- **Given** the running container **When** its published ports are inspected **Then** the dashboard port is bound to the host's loopback address only, and a request from another machine fails
- **Given** a request whose host name is not one the listener was configured with **When** it arrives **Then** it is refused (T35)
- **Given** the dashboard listener **When** its endpoints are listed by an architecture test **Then** every one is a read, and none appends a Signal, changes configuration, or touches a secret (AD-17)
- **Given** the Dashboard **When** it is loaded with the network observed **Then** it requests nothing from any other origin, sets no cookie, and the response carries a content security policy that allows no inline script
- **Given** the token value **When** logs and the database are searched for it **Then** it is found in neither
- **Given** `deploy/Dockerfile` **When** the image is built **Then** the Angular workspace under `src/Hephaestus.Dashboard` is built in a build stage, and the runtime image contains its static files and no Node.js (AR-72)
- **Given** a pull request **When** GitHub Actions runs **Then** the Angular workspace is built and tested, and the license check of Story 1.3 covers its packages (NFR-15)
- **Given** the installation documentation **When** an Owner reads it **Then** it explains how to issue a token and open the Dashboard, and says that the Dashboard is local and read-only

**New Story 1.15: See Projects and configuration in the Dashboard**

As the Owner, I want to see my registered Projects and how the installation is configured, so that I can check a setup without typing commands.

- Projects are listed with name, Base Branch, whether the Base Branch is protected, and the result of the last configuration check with the commit it was read from (FR-67)
- Trusted Users, Aliases, limits, and protected paths are shown; secrets are shown by name only
- A Project that was removed is shown as removed, with its history still reachable
- Every figure matches what the command line shows for the same record
- An installation with no Project shows an empty state that says how to register one

**New Story 2.12: See Tasks and their states in the Dashboard**

As the Owner, I want each Project's Tasks listed with their current state, so that I see the whole Queue in one place.

- Tasks are listed per Project with issue number, title, state, the Agents and Effort selected, declared dependencies, and a link to the issue (FR-67)
- A Task that is passed over shows why: an unsatisfied dependency or a label problem
- A Task waiting for the Owner shows its open question and links to where it is answered; the answer is given on GitHub, not here
- The list can be filtered by state
- Issue titles and bodies are rendered as text (T36)
- **Given** the Dashboard open on a Project **When** the loop commits a transaction that changes one of its Tasks **Then** the change stream sends one event naming the Task and the kind of change, and the new state is on screen without a reload (AR-74)
- **Given** the change stream **When** its events are inspected **Then** they carry ids and kinds only, and no title, body, or output
- **Given** a stream that was cut, or a Hephaestus restart **When** the Dashboard reconnects **Then** it reads everything on screen again, so a missed event leaves nothing stale
- **Given** a stream request without a valid token, or one beyond the bound on open streams **When** it arrives **Then** it is refused (T38)
- **Given** the loop **When** it announces a change **Then** it does so after the commit, through a Core port, and writes nothing to the journal for it

Rationale for placing the stream here: the loop it listens to arrives with Story 2.2, so Epic 1 has nothing to announce.

**New Story 3.22: Follow a Task's history in the Dashboard**

As the Owner, I want to open a Task and see every step it went through, so that I can work out what happened without the command line.

- A Task shows each step run with kind, Agent, Model, Effort, start and end, outcome or failure class, and usage, none where not reported (FR-67, FR-53)
- Validation results are shown per command with exit status and duration
- Stored, masked output of a step run can be opened while it is retained; after retention the view says it was removed
- The pull request and the Task branch are linked
- The history is unchanged after a restart and after the Workspace is removed
- A step that starts or ends while the Task is open appears without a reload; the output of a step that is still running is not shown until it ends (AR-66)
- The end-to-end harness of Story 3.3 opens the Dashboard for a completed Task and checks what is shown against the recorded history

**New Story 4.7: See Findings, the plan, and Correction rounds in the Dashboard**

- A Task shows its Findings with id, class, and current status, the plan when one was made, and each Correction round with what was returned to the Agent (FR-67)
- Finding and plan text are rendered as text (T36)

**New Story 6.4: See Schedules and windows in the Dashboard**

- Each Project shows its Schedule with its time zone, whether a window is open, and when the next one opens (FR-67)

**New Story 7.6: See Agent capacity and Handoffs in the Dashboard**

- Each Agent shows its capacity record: ready, exhausted until, last probe (FR-67)
- A Task waiting for capacity shows what it waits for and until when, where known; a Handoff appears in the Task's history with its cause

**New Story 9.6: See status and statistics in the Dashboard**

- A status view names the running Task and its step, waiting Tasks with what they wait for, and queued Tasks in dispatch order (FR-54)
- A statistics page shows SM-1 to SM-7, SM-C1, and SM-C2 as Story 9.3 computes them; each figure opens the Tasks it was computed from; a measure with no data is shown as none (FR-55)

**New Story 10.4: See which pull requests await what in the Dashboard**

- Each open pull request shows whether a Trusted User approved it, whether the approval covers the latest commit, and whether checks pass (FR-67, FR-41)

**New Story 11.8: See Stacks in the Dashboard**

- A Stack is shown in order with each pull request's state and its position; a pull request held as a draft above a failed Task says so (FR-67)

**A rule for every dashboard story.** It adds a view of data that an earlier story of the same epic records, it adds no endpoint that is not a read, and its view follows the change stream of Story 2.12. If, when an epic starts, its dashboard story turns out to show nothing the Owner needs, it is dropped with a note rather than built for symmetry.

### 4.7 Offered in the same pass, not required by this change

The readiness gate left three inexpensive findings open. The documents above are being edited anyway:

- Spine AD-15 and `requirements.md` FR-37 still contradict the owner decisions recorded in Stories 2.5 and 11.1.
- Story 3.7 is on the proof-of-concept path but needs the `confirm` command of Story 2.11, which is not.
- The step retry bound and the Handoff bound are "read from configuration" with no document saying which configuration.

## 5. Implementation handoff

**Scope: major.** The change alters product scope, one architecture decision, and the threat model. It is a replan of documents, not of code.

| Order | Skill | Responsibility | Done when |
| --- | --- | --- | --- |
| 1 | `bmad-prd` (update) | Section 4.1 | FR-67 and FR-68 exist; FR-55 and §7 are amended |
| 2 | `bmad-architecture` (update) | Sections 4.2 and 4.3; render `docs/` again | AD-17 has two listeners and the change stream; Stack pins Angular; the threat model has T34 to T38 |
| 3 | `bmad-spec` (update) | Section 4.4 | SPEC and companions agree with the PRD and the spine |
| 4 | `bmad-ux` | Section 4.5 | `DESIGN.md` and `EXPERIENCE.md` cover the Dashboard, the command line, and GitHub text |
| 5 | `bmad-create-epics-and-stories` | Section 4.6 | `epics.md` holds 92 stories and its coverage map lists FR-67 and FR-68 |
| 6 | `bmad-sprint-planning` | Refresh `sprint-status.yaml` | The gate passes or its concerns are accepted; ten new keys are in `backlog` |

Steps 1 and 2 do not depend on each other. Step 4 must finish before Story 1.4 starts, because that story adds the first command. Stories 1.1, 1.2, and 1.3 can be built while steps 1 to 6 run.

**Success criteria.**

- No document says the dashboard is out of scope, and none names React.
- A reader of the spine alone can tell which listener an endpoint belongs on.
- Every claim about the Dashboard's safety in user-facing material has a test named in the threat model's verification plan (NFR-8).
- The proof-of-concept path is still 30 stories.
