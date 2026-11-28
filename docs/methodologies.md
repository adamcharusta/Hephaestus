# Methodologies

Story 3.11, AD-12, AD-14, AD-21, FR-61, FR-62. A Methodology is how a step is to be done, kept as data: a directory in `methodologies/` of the image with a `manifest.yml` and one instruction template per Agent step kind. A Project names one in its configuration
(`methodology`, see [Project Configuration](project-configuration.md)); with no name, the built-in `default` is used. The same instructions go to whichever Agent performs a step, so moving a Task between Agents changes who does the work and not how.

## A template

A template is the file `<step kind>.md` of the Methodology, named with the kind's tag: `readiness`, `planning`, `implementation`, `correction`, `rework`, `review`. `default` holds `implementation.md` in this release, and a Methodology supports the kinds it has a file for.
Core chooses the file from the step kind alone: it does not look at the Methodology's name or at the Agent.

A template is a plain role prompt. It has no placeholders: nothing is substituted into it. It refers only to the context files by their paths under `/hephaestus/context` and to the repository at `/workspace`, and it relies on no skill, sub-agent, or slash command of an Agent CLI, and on
no instruction file in the repository, because neither CLI runs with them (AD-12). It tells the Agent to write the prose of its result in the language that `task.json` names, so that one template serves every language.

## The context files of a step run

Core writes one set for each step run. `instruction.md` is the template's bytes, unchanged; the Agent's prompt is one fixed sentence that points at it. The runtime places the set read-only at `/hephaestus/context`, and the Methodology's files read-only at `/hephaestus/methodology`.

| File | Holds | When it exists |
| --- | --- | --- |
| `instruction.md` | The template of the step kind | always |
| `task.json` | `taskId`, `stepRunId`, `repositoryId`, `issueNumber`, `branch`, `startCommit`, `baseBranch`, `stepKind`, `round`, `language` | always |
| `issue.md` | The issue's title and body as a Trusted User vouched for them | the Task has an admitted snapshot |
| `comments.json` | The admitted comments, oldest first: `commentId`, `authorLogin`, `body`, `answersEscalationId`, `admittedAt` | there is one |
| `plan.md` | The plan | there is one |
| `findings.json` | The Findings to fix | there are some |
| `validation.json` | The results of Validation | there are some |
| `handoff.md` | What earlier steps did, assembled by Core from recorded data | the step continues earlier work |
| `result.schema.json` | The JSON schema of the step's result | the step kind has a result type |

An input that does not exist is an absent file, never an empty one. Only content admitted under AD-15 is written into any file. The same step of the same Task gives the same bytes: nothing in the set depends on the Agent, the time, or the order of anything but the input.

## What a step run records

The Methodology's name and the identity of the template applied: its path in the Methodology and the SHA-256 of its bytes (`implementation.md@sha256:<hash>`), so that an edited template is another identity.

## The result of an implementation step

`Implementation`, `Correction`, and `Rework` give one document:

```json
{ "outcome": "completed", "summary": "…", "questions": [] }
```

`outcome` is `completed` or `needs-decision`; with `needs-decision`, each of `questions` has a `question` and its `options`. Every key is required and no other is allowed. The schema is generated from the Core record and holds only
what both CLIs' schema-constrained output accepts: objects, strings, enumerations, and arrays, with no reference, alternative, nullable type, or size constraint. A test fails when it holds anything else.

## What is not here yet

Templates for the other step kinds and the `bmad` Methodology (Epics 4 and 5), the files a Project supplies from its Base Branch, and the files a Project supplies. The loop assembles a set for each step run and records the Methodology and the template it applied on the step run ([Starting work and running a Task](dispatch.md)).
