# The pull request of a Task

Story 3.13, FR-36, AD-5, AD-19, [ADR-0029](adr/0029-publish-one-pull-request-per-task.md).

Each Task ends in one pull request from its Task branch (`hephaestus/<issue>-<title>`) to the Project's Base Branch, opened by the GitHub App under its own identity. Only `Hephaestus.GitHub` talks to GitHub: the loop asks it through `IPullRequests`,
and it has no call that merges, closes, or reviews a pull request.

## The description

Core composes the title and the description from the facts the publication's intent carries (`PullRequestFacts`), in the language of the Task. The sections are fixed by `EXPERIENCE.md`, in this order; a section with nothing to say is left out.

| Section | Content |
| --- | --- |
| What changed | The summary the implementing Agent returned, neutralised. |
| Task | `Issue #<n>`, which GitHub turns into a link. No closing keyword, so a merge does not close the issue. |
| How it was checked | A table of each Validation command, its result (`passed`, `failed` with the exit status, `timeout`, `interrupted`, `not-run`), and its duration. The result is what the runtime saw (ADR-0028). |
| Who did what | The Agent, Model, and Effort that implemented the Task. |

Results, statuses, Agent names, and Effort values are machine values and are never translated; the section titles and the sentences around them are, in `en` and `pl`.

Text that came from an Agent or a person is neutralised before it is placed: HTML comments are escaped, `@` mentions and issue-closing keywords (`closes #1`, `fixes owner/repo#2`) are broken with a zero-width space, and a `/hephaestus` command is fenced as code.
A Validation command is shown in a code span on one line, at most 200 characters. The gateway then masks the value of every stored secret in the title and the description.

## Publishing

Publishing is an effect of the loop, journaled before and after like every effect, and it is no step run: it takes no slot. Its intent names the step run and the tip; the facts are the intent's payload.

1. The gateway looks for the open pull request whose head is the Task branch. If there is one, because an earlier publication opened it, or because the host stopped after GitHub opened it and before the loop recorded that, its title and description are brought up to date. Otherwise one is opened.
2. When the result is recorded, a Task in `running` moves to `pull-request-open` in the same transaction, with the intent of its state label. A Task already in `pull-request-open` only has its pull request updated.
3. A Task that has ended or that waits is published nothing, and the intent ends as `superseded`.
4. A failure is treated as every effect on GitHub is: a wait for a rate limit is not a failure, an outage is retried at bounded delays, and a refusal for good, or the bound, ends the effect and raises an Escalation whose comment says the pull request could not be opened or updated.

The loop journals the intent when every Validation command of a Task has passed on its pushed branch ([Starting work and running a Task](dispatch.md)).
