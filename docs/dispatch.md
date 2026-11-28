# Starting work and running a Task

Story 3.14, FR-14, FR-16, FR-2, AD-1, AD-17, AD-18, [ADR-0030](adr/0030-start-a-project-on-demand-and-drive-a-task-from-its-records.md).

Nothing runs until you say a Project may be worked. Starting work on a Project opens a **window** on it; while a window is open, the loop dispatches the Project's `queued` Tasks one at a time, and each Task runs from the issue to a pull request. A window is how a
Schedule will work in v0.2; this story has the one you open yourself.

## The commands

```bash
docker exec hephaestus hephaestus project start acme/shop      # Signal 12 appended.  A window is open on acme/shop. Its Queue is worked until it is empty or you stop it.
docker exec hephaestus hephaestus project stop acme/shop       # The window on acme/shop is closed. A Task that is running finishes; no other starts.
docker exec hephaestus hephaestus signal show 12               # id: 12, kind: start-requested, status: applied, reason: none
```

Each is a request to the control API, which appends a Signal and answers `202` with its id (AD-17). `project start` and `project stop` print the id, wait up to ten seconds for the loop to apply it, and say whether it was applied or rejected;
`--no-wait` returns at once, and a Signal still pending after the wait is reported as pending with the `signal show` command that follows it, and that is exit code `0`. `--json` prints the Signal as the API answers it: `id`, `kind` (`start-requested`
or `stop-requested`), `status` (`pending`, `applied`, or `rejected`), and `reason`. A rejected Signal's reason is `project-not-registered`; an applied one that changed nothing says why: `window-already-open` for a second start, `window-not-open` for a stop with
no window. A Project that is not registered is refused by the command with a problem and nothing is appended.

A Project has at most one open window; the database refuses a second. A window closes when the Project has no `queued` Task left, which is also when you started work on a Project with nothing in its Queue, or when you stop it. A Task that is `running` finishes
all the same, and no other starts.

## Which Task starts

At most one Task runs in the whole installation (FR-14). Each tick, when none does, the loop takes the `queued` Tasks of the Projects with an open window, puts the one with the lowest issue number first (ties by repository id; the Priority Label and declared dependencies join the policy with
Stories 2.7 and 2.8), and starts the first that can be started. `Running` is entered only from `queued`, in one transaction with the Task's snapshot, its first step run, and the intent of its label, and the database refuses a second Task in `running`.

A Task that cannot be started is passed over, and the Tasks of other Projects are not held back: its Project's `hephaestus.yml` is missing or invalid, its image cannot be used, its Project names no primary Alias, or its Agent has no adapter, does not support the Effort or the sign-in mode,
has no sign-in chosen, or is known to have no capacity. The log says which and why, with the Task's id, and the Task is tried again after a minute. A comment on the issue is Story 3.15's.

## The snapshot

When a Task starts, the file is read from the Base Branch, validated as `project check` does, and kept with the commit it was read at and the image it resolved to: the reference, the digest, the architecture, and the C library. That is the Task's **snapshot**; every later step of the
Task, the Validation commands, the image, the limits, the egress hosts, and the Methodology, comes from it and never from the file again, so a change on the Base Branch cannot change a run that began before it. The commit is also the Task's start commit. A Task that is started again, after a
reply to an Escalation, keeps the snapshot it has.

## The steps

A step is recorded before it is carried out and the loop never waits for a container (AD-18): the step run is planned with its deadline, its container is asked for and left, and the runtime's step-exited Signal ends it. What comes next is decided from what is recorded, so a tick can stop anywhere.

| Step run | What the loop does |
| --- | --- |
| **Implementation** | The Primary Agent the Project's Alias names, signed in as you chose, on a fresh checkout of the start commit; its instruction, `task.json`, `issue.md`, and the admitted comments are the context files. When it ends, its output is read by the adapter and its result is validated by strict deserialization. |
| **Checkpoint and push** | A result that completed has its work kept as one commit, and the push of it is journaled in the same transaction. The push is an effect like any other, made through the gateway's checks (ADR-0024). |
| **Validation** | When the push is made, each of the Project's commands runs alone, in order, on a fresh checkout of the pushed tip, one container each, with its own step run. The first that does not pass ends Validation. |
| **Publication** | When every command has passed, the pull request is journaled for publication with the facts that its description is composed from (ADR-0029); its result moves the Task to `pull-request-open`, which frees the slot. |

Before every step the Task branch is fetched, as the gateway's checks need (ADR-0024): where someone else moved it, no step runs on it and an Escalation holds the Task until a Trusted User adopts the remote tip, which Story 3.18 delivers; a branch that cannot be read ends the step as an `infrastructure-error`.

A step run is `planned`, `started`, `ended`, `checkpointed`, or `concluded`; it records the Agent, Model, and Effort that performed it, the Methodology and template, the tips before and after, how its container ended, and the class of its failure.

A step that does not end well is classified and, in the classes that spend the step retry bound, attempted again; when a bound is spent the Task ends as `failed`, with its label and a comment on its issue, and keeps its branch ([`failures.md`](failures.md), Story 3.16). A tree the Agent left as it found
it, with nothing pushed earlier, ends the Task at once: there is nothing to publish.
A result that needs a decision raises an Escalation with the question and its options (one question is the decision with its options; several are one numbered decision with none), and the Task continues from your reply as a new step.

## What is not here yet

Recovery of a step whose container was lost with the host (Story 3.17), the refusal comments and the `Queued` Task that already has a step run and is dispatched with no window (Story 3.15), Review and Correction (Epic 4),
Schedules and the other windows (Epic 6), and cancel (Epic 9). Until Story 3.15, a Task that returns to `queued`, after an answer or because a Trusted User opted a failed Task in again, is dispatched only while its Project has a window open.
