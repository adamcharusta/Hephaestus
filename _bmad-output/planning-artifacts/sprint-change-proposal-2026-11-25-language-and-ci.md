---
title: Sprint Change Proposal — Polish as a second language; branch model, quality gates, and image channels
project: Hephaestus
date: 2026-11-25
status: approved
approved_by: Adam
approved_on: 2026-11-25
scope: moderate
mode: batch
follows: sprint-change-proposal-2026-11-25.md
---

# Sprint Change Proposal — language, and how Hephaestus's own code is built and released

## 1. Issue summary

**Trigger.** Two requests from the Owner on 2026-11-25, made while the dashboard change was being applied. No story has started.

1. **Language.** English is the default; Polish can be selected.
2. **CI/CD and code quality for Hephaestus's own repository.** SonarQube, at least 80% test coverage, two long-lived branches (`main` for production, `develop`), every change through a pull request, a `develop` image after a merge to `develop`, a production version after a merge to `main`.

**Decisions the Owner made (2026-11-25).**

| Question | Decision |
| --- | --- |
| What the language setting covers | Everything the Owner reads: the Dashboard, text on GitHub, and the command line's human output. |
| Is the repository public from the start | Yes. |
| Which SonarQube | SonarQube Cloud (`sonarcloud.io`), not a self-hosted server. |
| What 80% applies to | Both: all code, and new code. |
| How a version reaches `main` | Asked for a recommendation; section 3 gives it. |

**How this proposal reads two of those answers.** "Everything" is taken to include the command line's sentences and table headers, but not anything a program reads. "Not a self-hosted server, through sonarcloud.io" is taken to mean SonarQube Cloud. If either reading is wrong, section 4 changes.

**Evidence of conflict with the plan.**

- NFR-17 says source code, documentation, and public materials are in English and names no other language. `EXPERIENCE.md` says "English on all three surfaces".
- Story 1.3 builds and tests every pull request but names no branch model, no coverage threshold, and no static analysis.
- Story 1.13 publishes images only when a version tag is pushed. FR-59 lists "container image tags" as versioned public surface, which a moving `develop` tag is not.
- NFR-15 allows only free and open-source libraries and tools. SonarQube Cloud is a hosted, proprietary service.
- Threat T23 and AR-59 say workflows triggered by `hephaestus/*` branches should hold no secrets, because they run code an Agent wrote. Pull request analysis needs a token.

## 2. Impact analysis

### 2.1 Checklist record

| Item | Status | Finding |
| --- | --- | --- |
| 1.1 Triggering story | N/A | No story started. |
| 1.2 Core problem | Done | Two new requirements from the Owner. |
| 1.3 Evidence | Done | Section 1. |
| 2.1 to 2.5 Epics | Done | No epic added, removed, or reordered. Epic 1 gains one story; nine existing stories gain or change criteria. |
| 3.1 PRD | Action needed | One new requirement (FR-69), one new cross-cutting requirement (NFR-18), NFR-15, NFR-17, and FR-59 amended. |
| 3.2 Architecture | Action needed | One new decision (AD-21, language), Build and Public surface conventions, deployment seed. |
| 3.3 UX | Action needed | Language rules in `EXPERIENCE.md`; the theme switch gains a neighbour. |
| 3.4 Other artifacts | Action needed | Threat model (one new threat), GitHub Actions workflows, repository settings. |
| 4.1 Direct adjustment | Viable | Effort medium, risk low to medium. |
| 4.2 Rollback | Not viable | Nothing is built. |
| 4.3 MVP review | Not needed | Scope grows slightly; nothing is cut. |
| 4.4 Selected path | Done | Direct adjustment. |
| 6.3 Owner approval | Done | Approved by the Owner on 2026-11-25. Both readings in section 1 confirmed. Amended on approval: SonarQube Cloud's secretless analysis replaces the stored token (sections 3.2, 4.3, Story 1.16). |
| 6.4 `sprint-status.yaml` | Done | Regenerated on 2026-11-25: 12 epics, 93 stories, one new key (`1-16-…`) in `backlog`, nothing dropped. |

### 2.2 Story impact

| Story | Change |
| --- | --- |
| 1.2 BMAD spike | One criterion added: how a BMAD workflow is told which language to write in. |
| 1.3 Solution skeleton | Branch model, pull-request-only rule, coverage threshold in the build. |
| 1.4 Run with Docker Compose | The message mechanism: every sentence the command line or the API produces comes from resources in English and Polish. |
| 1.9 Aliases, limits, protected paths | The installation's language setting. |
| 1.11 Project configuration file | An optional `language` key. |
| 1.13 Publish versioned releases | Rewritten: `develop` channel image; a merge to `main` publishes the version the repository states. |
| 1.14 Open the Dashboard | Two locales and a language switch; a coverage threshold for the Angular workspace. |
| **1.16 (new)** | Hold every pull request to the quality gate (SonarQube Cloud). |
| 2.5 Say what was ignored | Comment text comes from resources in the Task's language. |
| 3.11 Same instructions for every Agent | The Task's language reaches the Agent as data. |
| 8.2 Alert the Owner | Notifications follow the installation's language. |

Totals: 92 stories become 93. The proof-of-concept path stays at 30; Story 1.16 is not on it and can be built beside it as soon as Story 1.3 is done.

### 2.3 Technical impact

- **Language** touches every story that writes a sentence. The cost is paid once if the mechanism exists before the first sentence does, which is why it goes into Story 1.4 and not into a late story.
- **CI/CD** changes how every story is delivered: against `develop`, through a pull request that must pass the build, the tests, the coverage threshold, the license check, and from Story 1.16 the quality gate.
- **Self-hosting.** When Hephaestus works on its own repository, its Base Branch is `develop`. Its pull requests are held to the same checks as a person's.

## 3. Recommended approach

**Direct adjustment.** Both requests fit the existing structure.

### 3.1 Language

- **One rule: translate what a person reads, never what a program reads.** Never translated: command names and options, `--json` output, label names and state names, `/hephaestus` verbs, `Depends-On:`, hidden markers, commit trailers, configuration keys, log lines, and Glossary terms (Task, Validation, Finding stay as they are in Polish text, because they are also label and command vocabulary).
- **Where the language is set.** The installation has a language, `en` by default. A Project may name its own in `hephaestus.yml`, for the text Hephaestus writes on that Project's issues and pull requests. The Dashboard starts in the installation's language and has a switch, remembered per browser like the theme.
- **Text an Agent writes** (summaries, Findings, plans, questions) follows the Task's language without a second set of Methodology templates: the language travels in `task.json`, and the template tells the Agent to write prose in the language named there. Templates stay identical for every Agent and contain no substitution, as AD-14 requires.
- **Source code, documentation, and public materials stay English** (NFR-17).

**Recommended, the Owner did not choose it:** the per-Project override. Without it, one installation working on a public English-language repository and a private Polish one would write the same language on both.

### 3.2 Branches, quality, and releases

- **Two protected branches.** `develop` is the default branch and the target of every feature pull request. `main` receives pull requests from `develop`, and from a `hotfix/*` branch when production must be fixed first. Nothing is pushed to either directly.
- **80% twice, by two different mechanisms.**
  - *All code:* the build itself fails below 80% line coverage, in the .NET solution and in the Angular workspace. It is deterministic, needs no service, and can therefore also be a Validation command when Hephaestus works on itself, so an Agent that lowers coverage gets it back in the Correction Loop.
  - *New code:* SonarQube Cloud's built-in quality gate, which requires 80% coverage on new code, on every pull request.
  - Splitting it this way means nothing depends on a custom quality gate, which SonarQube Cloud may not offer on the free plan.
- **SonarQube Cloud and NFR-15.** NFR-15 is clarified, not relaxed: it governs what an installation of Hephaestus needs to run. Services used only to build Hephaestus's own repository (GitHub Actions, the container registry, SonarQube Cloud) are not dependencies of an installation. Nobody who runs Hephaestus needs a SonarQube account.
- **No stored token.** Pull request analysis runs in a workflow that also runs code from the pull request, and from v0.1 on some of that code is written by an Agent. A long-lived secret there would be the one real trade-off of this change. SonarQube Cloud's secretless analysis, available on every plan since 16 November 2026, avoids it: the workflow proves its identity with a GitHub-issued OpenID Connect token and receives a credential that lasts thirty minutes and can only submit an analysis for this one project. Nothing is stored in the repository's secrets. **Amended on approval:** this replaces the stored token of the first draft, whose claim of a project-scoped token was wrong for the free plan, where only personal tokens carrying the whole account's permissions exist. What remains is small and is recorded in the threat model: code in the same job could use the short-lived credential to submit a forged analysis of this public project.
- **Pull requests from forks** cannot obtain the OpenID Connect token, so they get no SonarQube analysis; the build's own 80% threshold still applies to them.
- **Images.**
  - A merge to `develop` publishes `hephaestus:develop` and `hephaestus:develop-<short commit id>` (and the same for `hephaestus-tools`). `develop` is a moving channel for trying what is next. It is not a release and not part of the versioned public surface.
  - **Recommended for `main`: publish on merge, from the version the repository states.** One file holds the version. A pull request to `main` fails unless that version is a valid three-part Semantic Version, is higher than the last one published, and has a changelog entry. The merge then creates the git tag and publishes the images under that version. A version with a pre-release identifier (`0.1.0-rc.1`) publishes under its own tag and leaves the stable tags where they are.
  - Why not a hand-pushed tag, as Story 1.13 has it today: with the tag made by hand, "merged to `main`" and "released" can drift apart, and the check that the version and the changelog are in order runs after the merge instead of before it. Publishing on merge makes the pull request to `main` the release decision, which is what the Owner described.
- **Nothing here deploys anything.** Publishing an image is not deploying it. Hephaestus still never updates or redeploys itself.

**Cost.** Effort medium: one new story, nine amended, four documents. Risk low for language, low to medium for CI (the token trade-off above). The proof-of-concept milestone is not delayed.

## 4. Detailed change proposals

### 4.1 PRD (`prd.md`)

**New requirement in §4.13 Distribution and Versioning, or its own feature "Language"**

> **FR-69: Language** [v0.1]
>
> Hephaestus speaks English by default, and the Owner can choose Polish.
>
> Consequences (testable):
> - The Owner sets the installation's language from the command line. The Dashboard, the command line's sentences and table headers, Notifications, and the text Hephaestus writes on GitHub follow it.
> - A Project can name its own language for the text Hephaestus writes on its issues and pull requests.
> - The Dashboard's language can also be switched in the browser.
> - Text an Agent writes for the Owner to read is written in the Task's language.
> - Nothing a program reads changes with the language: command names and options, machine-readable output, labels, comment commands, dependency declarations, and configuration keys are the same in every language.
> - No sentence is missing in a supported language: a build that lacks a translation fails.
>
> Out of scope: languages other than English and Polish; translating the Glossary's terms.

**NFR-17 — amend**

OLD:
> **NFR-17: English.** Source code, documentation, and public materials are written in English.

NEW:
> **NFR-17: English.** Source code, documentation, and public materials are written in English. What Hephaestus says to the Owner while running follows the language the Owner chose (FR-69).

**NFR-15 — amend (add one sentence)**

> This governs what an installation needs in order to run. Services used only to build and check Hephaestus's own repository are not dependencies of an installation.

**New cross-cutting requirement, §5.4 or a new §5.6 "Engineering quality"**

> **NFR-18: Quality gate on Hephaestus's own code.** A change reaches `develop` or `main` only through a pull request that passes the build, every test, static analysis, and test coverage of at least 80%, measured both over all code and over the code the change adds. This holds for changes Hephaestus makes to itself.

**FR-59 — amend**

- Consequence added: "A merge to `main` publishes the version the repository states; a pull request to `main` cannot merge with a version that is not valid, not higher than the last one, or missing from the changelog."
- Consequence added: "A merge to `develop` publishes a moving `develop` image. It is a preview channel, not a release, and not part of the versioned public surface."

**§7.1 v0.1 — add**

> - Language: FR-69.

### 4.2 Architecture spine

**New decision**

> **AD-21 — Language is data; the machine surface has one language**
>
> - **Binds:** Core, Host, GitHub gateway text, `Hephaestus.Dashboard`, context assembly; FR-69, NFR-17
> - **Prevents:** a sentence built by string concatenation in an adapter; a translated label, command, or JSON key; two modules deciding the language differently; a second set of Methodology templates per language
> - **Rule:**
>   - Every sentence Hephaestus produces for a person is a message id with parameters, resolved in Core from resource files for `en` and `pl`. Adapters and API handlers pass ids and parameters; none composes prose. A test fails when an id has no entry in a supported language.
>   - The language is resolved in one Core function: for text on GitHub and for Agent prose, the Task's configuration snapshot (the Project's `language` key, else the installation's); for the command line and Notifications, the installation's; for the Dashboard, the browser's choice, else the installation's.
>   - Never translated: command names and options, `--json` output, problem `type` values, label and state names, comment-command verbs, `Depends-On:`, hidden markers, commit trailers, configuration keys, log lines, Glossary terms.
>   - `task.json` carries the Task's language. Methodology templates stay single-language and contain no substitution (AD-14); they instruct the Agent to write prose fields in the language `task.json` names.
>   - The Dashboard ships both languages in the image; switching needs no rebuild and no request to another origin (AD-17).

How the Dashboard holds its two languages (Angular's built-in localisation with one bundle per language, or a runtime translation library) is decided in Story 1.14 and recorded there.

**Consistency conventions — amend**

- *Build:* "…GitHub Actions builds and tests every pull request and publishes images on a version tag." becomes "…Two protected branches: `develop`, the default, and `main`. Every change arrives by pull request. GitHub Actions builds and tests every pull request; the build fails below 80% line coverage in the .NET solution and in the Angular workspace; SonarQube Cloud analyses every same-repository pull request and both branches. A merge to `develop` publishes the `develop` images; a merge to `main` tags and publishes the version the repository states."
- *Public surface:* add "The `develop` image tags are a preview channel outside the versioned surface."

**Stack — add**

| Name | Version |
| --- | --- |
| SonarQube Cloud, with the scanner for .NET and secretless analysis | hosted; scanner and action versions pinned by Story 1.16 |

**Deployment and operations — amend** the Images line with the two channels.

**Deferred — add**

| Decision | Why it can wait | Decided in |
| --- | --- | --- |
| Languages beyond English and Polish | Adding one is adding resource files (AD-21). | When asked for |

### 4.3 Threat model

**New threat, §4.4 Acting on GitHub**

| # | Threat | Mitigation | Residual |
| --- | --- | --- | --- |
| T39 | **The analysis credential used by code from a pull request**, including code an Agent wrote, because pull request analysis runs in the workflow that builds and tests that code. | No long-lived token exists: the workflow exchanges a GitHub-issued OpenID Connect token for a credential that lasts thirty minutes and can only submit an analysis for this one project. Workflow files are a protected path, so a Task cannot change them (AD-19). The repository's OpenID Connect identity is trusted by nothing else. Pull requests from forks cannot obtain it. | Low, accepted. Code in the same job could submit a forged analysis of a public project within those thirty minutes. |

**§7 Known limitations — add**

> Hephaestus's own continuous integration lets a workflow that runs pull request code obtain a short-lived analysis credential. Owners who copy this setup for their Projects should weigh the same trade-off (see T23), and should not replace it with a stored token.

### 4.4 UX (`EXPERIENCE.md`, `DESIGN.md`)

- Voice and Tone: "English on all three surfaces (NFR-17)" becomes the rule of section 3.1, with the list of what is never translated.
- Top bar: a language switch beside the theme switch; the choice is remembered in that browser.
- Command Line, Output: sentences and table headers follow the installation's language; everything listed as machine surface does not; `--json` is identical in every language.
- Text on GitHub: templates exist in both languages; the headline words and the "Next:" marker are translated; an Escalation's fixed sentences are translated and the Agent's question is in the Task's language.
- New UX requirement **UX-DR-20** carrying the above. `DESIGN.md` needs no change.

### 4.5 Spec

Re-derived after 4.1 and 4.2: FR-69 under CAP-13 or a new CAP-16 "Language"; NFR-15, NFR-17, NFR-18; FR-59; one constraint ("what a program reads is never translated"); `release-scope.md` gains FR-69 in v0.1.

### 4.6 Epics and stories

**Inventory.** Add FR-69, NFR-18, AR-75 (AD-21), AR-76 (branch model, coverage threshold, quality gate), AR-77 (image channels and release on merge), UX-DR-20. Amend NFR-15, NFR-17, FR-59, AR-2, AR-38. Coverage map: FR-69 to Epic 1 (extended in Epics 2, 3, and 8).

**Story 1.2 — add**

> **Given** a Task whose language is Polish
> **When** a BMAD workflow is run for it under each Agent
> **Then** the report states how the workflow is told which language to write in and whether its output follows it (FR-69)

**Story 1.3 — amend and add**

OLD:
> **Given** a pull request against the repository
> **When** it is opened or updated
> **Then** GitHub Actions builds the solution and runs every test, and reports the result on the pull request

NEW:
> **Given** the repository
> **When** its branches are inspected
> **Then** `develop` is the default branch, `develop` and `main` are protected, neither accepts a direct push, and the documentation says how a feature, a release, and a hotfix reach each (AR-76)
>
> **Given** a pull request against `develop` or `main`
> **When** it is opened or updated
> **Then** GitHub Actions builds the solution and runs every test, and reports the result on the pull request
> **And** the build fails when line coverage over all code is below 80%, with exclusions kept in one file where each has a stated reason (NFR-18)
> **And** these checks are required before a merge

**Story 1.4 — add**

> **Given** any sentence the command line or the API produces
> **When** it is written
> **Then** it is a message id with parameters, resolved in Core from resources for English and Polish; no handler or adapter composes prose (AD-21)
> **And** a test fails when a message id has no entry in a supported language
>
> **Given** the version command run with the language set to Polish
> **When** its output is compared with the English output
> **Then** the sentences differ and the command name, the options, and the `--json` document are identical (FR-69)

**Story 1.9 — add**

> **Given** a running installation
> **When** the Owner sets the installation's language to `en` or `pl`
> **Then** it is stored, shown with the rest of the Installation Configuration, and used by the command line from the next command; any other value is refused; a new installation is in English (FR-69)

**Story 1.11 — amend** the list of permitted keys to include `language` (`en` or `pl`), and add:

> **Given** a file that sets `language`
> **When** a Task of that Project starts
> **Then** the language is snapshotted onto the Task with the rest of the configuration, and a file without the key uses the installation's language

**Story 1.13 — rewritten**

> As someone installing Hephaestus, I want every release to carry a Semantic Version, published images, and a changelog entry, and a preview image of what is coming, so that I know what I am running and what changed.
>
> - **Given** a pull request merged to `develop` **When** the workflow runs **Then** the `hephaestus` and `hephaestus-tools` images are built for linux/amd64 and linux/arm64 and published as `develop` and as `develop-<short commit id>` (AR-77)
> - **Given** the `develop` tags **When** the documentation describes them **Then** it calls them a preview channel that is not a release and not part of the versioned public surface
> - **Given** a pull request to `main` **When** its checks run **Then** it fails unless the version the repository states is a valid three-part Semantic Version, is higher than the last version published, and has an entry in `CHANGELOG.md` grouped as added, changed, fixed, removed, and security (FR-59)
> - **Given** a version with a fourth numeric part **When** the check runs **Then** it fails
> - **Given** a pull request merged to `main` **When** the workflow runs **Then** it creates the git tag `v<version>` and publishes both images under that version
> - **Given** a version with a pre-release identifier, such as `0.1.0-rc.1` **When** it is published **Then** it gets its own tag and the stable tags do not move
> - **Given** a 0.x minor release with a breaking change to the public surface **When** its changelog entry is read **Then** the entry says so
> - **Given** a running container started from a published image **When** the Owner asks for the version **Then** the answer is the version of the tag the image was built from, or the commit id for a `develop` image
> - **Given** a failure after the tag was created and before both images were published **When** the workflow is run again **Then** it completes the publication and creates nothing twice
> - **Given** the documentation **When** it describes versioning **Then** it names the versioned public surface: the configuration format, the label and comment-command conventions, the command-line interface, and the release image tags

**Story 1.14 — add**

> **Given** the Dashboard **When** the Owner uses the language switch **Then** every label, hint, and empty state changes between English and Polish, the choice is remembered in that browser, and nothing is requested from another origin; state names, ids, commands, and text from GitHub or an Agent do not change (FR-69, UX-DR-20)
> **And** the way the two languages are held is recorded in this story's decision record
>
> **Given** a pull request **When** the Angular workspace's tests run **Then** the build fails when its line coverage is below 80% (NFR-18)

**New Story 1.16: Hold every pull request to the quality gate**

> As the Owner, I want every pull request analysed and held to a quality gate, so that neither a person nor an Agent can lower the quality of Hephaestus's own code unnoticed.
>
> - **Given** that the SonarQube Cloud organisation and project belong to the Owner's account **When** this story is planned **Then** the Owner binds the project to the repository and switches on secretless analysis for GitHub Actions in the organisation's settings; the story is not closed by a dev agent alone
> - **Given** a pull request from a branch of this repository, against `develop` or `main` **When** it is opened or updated **Then** SonarQube Cloud analyses it with the coverage of the .NET solution and of the Angular workspace, and reports the result on the pull request
> - **Given** an analysed pull request **When** its quality gate is evaluated **Then** it fails when coverage on new code is below 80% or the built-in gate's other conditions are not met, and a passing gate is required before a merge (NFR-18)
> - **Given** a merge to `develop` or `main` **When** the workflow runs **Then** that branch is analysed
> - **Given** the workflow **When** it is inspected **Then** it stores no SonarQube token: it obtains a short-lived credential by secretless analysis immediately before the scanner needs it, its job permissions are `id-token: write` and `contents: read` and nothing else, and the repository holds no `SONAR_TOKEN` secret (T39)
> - **Given** a pull request from a fork **When** it is opened **Then** it cannot obtain the credential and no analysis runs; the build's own coverage threshold and tests still apply, and the documentation says so
> - **Given** a pull request opened by Hephaestus from a `hephaestus/*` branch **When** it is opened **Then** it is held to the same checks as any other
> - **Given** the documentation and the threat model **When** they are read **Then** they state that SonarQube Cloud is used to build Hephaestus and is not needed to run it (NFR-15), and they record the accepted risk of T39

**Story 2.5 — add**

> **Given** a comment Hephaestus posts on an issue of a Project **When** its fixed text is written **Then** it comes from the resources in the Task's language, or the Project's when no Task exists; labels, the command syntax it mentions, and the hidden marker are the same in every language (AD-21)

**Story 3.11 — add**

> **Given** a step run **When** its context is assembled **Then** `task.json` carries the Task's language, and the `default` templates tell the Agent to write the prose fields of its result in that language; the templates themselves are the same for every language and every Agent (AD-21, AD-14)

**Story 8.2 — add**

> **Given** a Notification **When** it is composed **Then** its text is in the installation's language (FR-69)

**Proof-of-concept path.** Story 1.16 is added to "Stories that follow the milestone" for Epic 1.

## 5. Implementation handoff

**Scope: moderate.** One new requirement, one new decision, one new story; no epic changes.

| Order | Skill | Responsibility |
| --- | --- | --- |
| 1 | `bmad-prd` (update) | Section 4.1 |
| 2 | `bmad-architecture` (update) | Sections 4.2 and 4.3; render `docs/` again |
| 3 | `bmad-spec` (update) | Section 4.5 |
| 4 | `bmad-ux` (update) | Section 4.4 |
| 5 | `bmad-create-epics-and-stories` | Section 4.6 |
| 6 | `bmad-sprint-planning` | Refresh `sprint-status.yaml`: one new key |

**Owner actions outside the documents**, needed by Story 1.3 and Story 1.16: make the repository public; create `develop` (done) and protect both branches; create the SonarQube Cloud project (done), bind it to the repository, and switch on secretless analysis for GitHub Actions under Administration, Organization settings, Analysis. No token is generated or stored.

**Success criteria.**

- A Polish installation produces Polish sentences on all three surfaces and byte-identical machine output.
- No pull request can merge to `develop` or `main` below 80% coverage, measured both ways.
- A merge to `develop` yields a `develop` image; a merge to `main` yields exactly one new version.
- The threat model states the token trade-off plainly.
