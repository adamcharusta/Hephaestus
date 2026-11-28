# ADR-0031: Classify every failure, attempt a failed step again from the records, and bound the Task's time where the database sees it change

- **Status:** Accepted
- **Date:** 2026-11-28
- **Spine:** AD-1, AD-5, AD-9, AD-11, AD-16, AD-18

## Context

Story 3.14 ended a Task at the first step that failed. A blink of GitHub or Docker then costs the Owner an opt-in, nothing on the issue says what was attempted, the work of an Agent that ran out of time was lost with its container, and a Task's steps can together outlast its
duration limit, because each was bounded alone. ADR-0017 fixed the classes, their order, and the two bounds; this record says how the loop applies them so that a tick can still stop anywhere (AD-1, AD-18).

## Decision

**A failed step is recorded as it failed, and what follows is decided from the record.** Applying the step-exited Signal writes the step run's end with its class and moves nothing else; the class is the runtime's cause, then the adapter's reading, then Core's reading of the result (`FailureClassifier`).
The Task's next advance finds a step run that ended with a failure and decides: the work of an Agent step whose container ran is kept first, then the step is attempted again or the Task ends (`StepRetries`, a pure function of the class, the attempt, the bound, and the time left). A step that could not be carried out (a mirror that cannot be
read, a checkout that fails) ends the same way, so the infrastructure errors the loop meets are bounded by the same count as those the runtime reports.

**An attempt is a column and a retry is one transaction.** A step run has an `Attempt`, from 1. Concluding the failed step run and planning its retry, with the next attempt, the same Agent, Model, Effort, Methodology and template, a deadline, and the last pushed tip, is one change of the unit of work (`StepRunChange.Retry`), made only while the
Task still runs and only once. The bound is `stepRetries`, an installation setting (the spine puts it in the Installation Configuration), two by default, from 0 to 10; a step has that many attempts again and one, and each step of a Task has its own count, so a Validation command that passed is not run again for the failure of the next.

**The work of a failed Agent step is pushed before anything is decided.** FR-20 and the Task branch holding all work done need it, and a retry starts from "the last pushed tip", which would otherwise be the tip before the failed attempt. The checkpoint and the push are the ones a step that ended well takes, so the gateway's checks apply; a push they refuse
raises the Escalation they always raise, and the Task leaves `running` with the failure still recorded. A checkpoint that cannot be made after five ticks leaves the work behind, says so in the log, and goes on to the decision.

**The Task's time in `running` is a fact of the Task, kept by a trigger.** Where code moves a Task out of `running` is not one place (a step's end, an Escalation, an answer, an effect's result), and a sum over step runs misses the time between them. The migration adds `RunningMillis` and `RunningSince` to the Task and a trigger that, whenever the state changes,
adds the time since the Task entered `running` when it leaves, starts a stretch when it enters, and begins with none when a `failed` Task moves on. A step's deadline is the narrowest of its own limit and the time the Task has left; a Task that has spent its time (to within a second, because a deadline is set a moment before the Task's clock is read) is not attempted again, and its comment names the limit.
The limit is the Project's, held to the installation's, as the Agent's step limit was. An Agent's step has no shorter limit of its own, so an Agent's step that the runtime stops at its deadline has used the Task's time and is not attempted again; a Validation command has its own, shorter limit (30 minutes), and a command stopped at that is a `timeout` that is attempted again while the Task has time left.

**A failed Task ends with one comment, composed by Core from the facts its intent carries.** The comment has the shape of `EXPERIENCE.md` (headline, facts, "Next:"): the class and step, the command and its exit status for Validation, the attempts and the bound or the limit, and the branch. The facts are the payload of the comment's intent, as a pull request's are, so a restart composes the same text; the Project's own command text is neutralised.

**A Trusted User's Marker Label returns a failed Task to `queued`.** The lifecycle row `Failed --> Queued: opted in again` is applied by the loop like the rows for a waiting Task, in the transaction that marks the Signal, with the intent of the label. The Task keeps its snapshot, branch, and step runs; its next Implementation has attempt 1.

## Alternatives considered

- **Planning the retry in the transaction of the step-exited Signal.** It would need the retry's deadline, tip, and bound before the work was pushed, and the push is an effect of a later phase; deciding from the record after it keeps both decisions where their facts are.
- **Summing step runs for the Task's time.** It misses the gaps between steps and the time of a step that has not ended, and it would have to be kept in step with every place that moves a Task.
- **Counting retries per Task.** One flaky Validation command would then use the budget of the Implementation after it; the spine says the bound is of "that step kind".
- **Attempting a failed Validation command again.** A `validation-failure` spends a Correction round (ADR-0017), which Epic 4 builds; until then it ends the Task.
- **Treating an unidentified usage limit as a limit.** ADR-0017 rejects it: Hephaestus would wait for a reset that never comes.

## Consequences

- A Task that cannot be carried on because GitHub or Docker is down is attempted again up to the bound, and then fails; waiting for an outage without failing is the effect journal's (ADR-0007) for effects on GitHub, and Story 3.17's for a step lost with the host.
- A failed Task is dispatched again only while its Project's window is open until Story 3.15 makes a Task that has a step run dispatch without one.
- The comment on the issue for a tree the Agent left as it found it is the general one for a failure; the sentence that says the Agent made no changes is Story 3.15's.
- A test that fakes the unit of work must follow its rules for attempts, retries, and the time (`LoopStores`); the real one is tested against the database, trigger included.
