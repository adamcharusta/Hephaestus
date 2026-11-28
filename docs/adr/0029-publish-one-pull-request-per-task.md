# ADR-0029: Publish one pull request per Task as a journaled effect of the loop, composed by Core, found by its head branch

- **Status:** Accepted
- **Date:** 2026-11-28
- **Spine:** AD-1, AD-4, AD-5, AD-19

## Context

A Task ends in one pull request that tells the Owner what was done and how it was checked, so that review starts from facts and not from an Agent's summary alone (FR-36). The pull request is opened under Hephaestus's own identity, from the Task branch to the Project's Base Branch. Three things can go wrong: a crash after GitHub opened the pull request and before the loop recorded it must not open a second one (FR-21); text an Agent wrote must not become a way to act on GitHub in Hephaestus's name (AD-19); and a publication must not hold the single slot, because it is not a step run.

## Decision

**An effect of the loop.** `EffectKind.PullRequest` joins the journal beside the label, the comment, the notification, and the push. Its intent is journaled with the state change that causes it; its result is its own transaction. It is made by the loop through `IPullRequests`, which only `Hephaestus.GitHub` implements, and it is no step run and takes no slot.

**The facts travel with the intent.** The intent's detail names the step run and the tip (`pull-request|<step run>|<tip>`); the facts the text is composed from (`PullRequestFacts`: the issue's title, the Agent's summary, the Agent, Model, and Effort that implemented the Task, and each Validation command's status, exit status, and duration) are the intent's payload, a new nullable column of the journal. They are recorded data, kept so that a restart composes the same description from what was recorded and not from what happens to be in memory. The Validation results are those of `ValidationRun`, which an Agent's report never reaches (ADR-0028).

**Composed by Core, neutralised first.** `PullRequestDescription` writes the sections of `EXPERIENCE.md` in order: What changed, Task, How it was checked, Who did what, and leaves out one with nothing to say. The fixed words are resources in the Task's language. What an Agent or a person wrote goes through `AgentText.Neutralise`: HTML comments are escaped, mentions and issue-closing keywords are broken, and a `/hephaestus` command is fenced. A Validation command is shown as a code span on one line, cut at 200 characters, with no backtick or pipe that could end the span or the cell. The gateway masks stored secret values last, in the title and the description of an opened and of an updated pull request.

**No closing keyword.** The Task section says `Issue #7`. GitHub links the two, and a merge does not close the issue. A keyword would close the issue on a merge into the default branch and not on a merge into another Task's branch, so the same Task would behave differently by where its pull request is merged, and Hephaestus never closes an issue itself. The Owner was not asked (the run is unattended); the default is recorded in `EXPERIENCE.md` as an assumption and is one line to change.

**Found by the head branch.** `IPullRequests.PublishAsync` looks for the open pull request of the Task branch, the head qualified by the repository's owner as GitHub's filter wants it, before it opens anything. One that exists, made before a crash and not recorded, or made by an earlier publication of the same Task after further work, has its title and description brought up to date; only a branch without one gets a new pull request. The request can name only a Task branch as its head, and the gateway has no call that merges, closes, or reviews.

**The result moves the Task.** In the transaction that records the result, a Task in `running` moves to `pull-request-open` and the intent of its state label is journaled (`EffectRecording.Move`, `TaskMove`), so the label follows from the same commit and a crash between them cannot show a state the Task does not have. The move is made only while the Task is where the publication found it. A Task already in `pull-request-open` only has its pull request updated. A Task that has ended, or that waits, is published nothing: the intent ends as `superseded`, and a publication for that Task is journaled again when it gets there.

**Failure** is the same as for every effect on GitHub: a wait is not a failure, an outage is retried at the bounded delays, and at the bound, or on a refusal for good, the effect ends and an Escalation names it (its comment says that the pull request could not be opened or updated).

## Alternatives considered

- **Open the pull request from the runtime or the step.** No GitHub credential enters a Workspace, and a publication is not a step; only the gateway talks to GitHub.
- **A hidden marker in the description, as comments have.** Anyone who can edit a pull request can write one, and the branch name is already unique to the Task and what the story asks for.
- **A table of publications beside the journal.** The journal already has the intent, the retries, and the result, and an intent that carries its facts needs one nullable column and no second place that must agree with it.
- **Composing the description from the live records when the effect is made.** A restart, or a retry an hour later, would then compose a different text from the one the intent was journaled for.

## Consequences

- The loop (Story 3.14) journals the intent when Validation has passed and the branch is pushed, with `PullRequestIntent.ToNewEffect` and `PullRequestFacts.From`; until then nothing journals one.
- Epic 4 adds the Reviewer, the Findings, the plan, and the draft form for a failed Task to `PullRequestFacts` and to the description; Epic 11 adds the Stack.
- The pull request's number is not stored; a later story that needs it finds the pull request by its head branch, as this one does.
