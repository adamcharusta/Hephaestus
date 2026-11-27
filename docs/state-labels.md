# A Task's state on its issue

Every Task shows its state on its own issue as one label, `hephaestus:<state>`, the state in kebab-case. GitHub's issue filter on that label
lists every Task in that state: `is:issue label:"hephaestus:waiting-for-owner"` finds the ones that need you, with no Dashboard.

| Label | The Task is |
| --- | --- |
| `hephaestus:queued` | waiting to be worked on |
| `hephaestus:running` | being worked on |
| `hephaestus:waiting-for-owner` | waiting for a decision, or for an effect on GitHub that could not be made (see below) |
| `hephaestus:waiting-for-capacity` | waiting for an Agent to have capacity again |
| `hephaestus:pull-request-open` | done, with a pull request open |
| `hephaestus:failed` | failed |
| `hephaestus:cancelled` | cancelled |
| `hephaestus:withdrawn` | withdrawn before work began |
| `hephaestus:merged` | merged |
| `hephaestus:closed` | closed |

The label is a **projection** of the state Hephaestus keeps in its database; GitHub is not where the state lives. An issue carries exactly
one state label. Labels that are not state labels (`bug`, the Marker Label `hephaestus`, a label you made up under the prefix such as
`hephaestus:bogus`) are never touched.

Today a Task is created `queued` and nothing yet moves it on its own, so `hephaestus:queued` is the label you will see, and
`hephaestus:waiting-for-owner` when an effect could not be made. The other states take their label the same way when the stories that
reach them arrive.

## When it appears

The loop shows the state right after it commits it, so the label is on the issue within the loop's next tick, a second or so after the
Marker Label event reached Hephaestus, which is within one polling cycle of the label being applied ([`polling.md`](polling.md)).

## When someone changes it by hand

If anyone adds, removes, or replaces a state label, GitHub reports it as an event like any other, and the loop **writes the Task's state back**
on its next tick after the polling cycle that read the event. It does not matter who it was, and the event decides nothing about the Task:
whether the label is right is found by reading the issue's labels as they are then. What Hephaestus wrote itself is not answered, so it
cannot answer its own change for ever. Several changes to one issue while the Task's label is still waiting to be made are answered by
that one effect, which reads the labels when it is made: whoever can label an issue cannot make Hephaestus ask GitHub once for each change.

## The effect journal

A label change is an outside effect, so it is journaled (ADR 0007). A comment is one too, and is journaled the same way; see
[`intake.md`](intake.md#what-hephaestus-says-on-the-issue-when-it-ignores-something). Applying a Signal, the state change and the **intent** to show it are
committed together. The change is made after that commit, and its **result** is recorded after it was made:

- An intent has an idempotency key, `<scope>:<cause>:<effect-kind>:<ordinal>`, for example
  `0197a1b2-c3d4-7e5f-8a6b-7c8d9e0f1a2b:g12:label:0`: the Task's id, `g` and the sequence of the Signal that caused it, `label`, and how
  many effects of that kind that cause already had for the Task. The gateway is handed the key with every change.
- A result is `applied`, `already-in-place` (the issue was found as wanted, so nothing was changed), `superseded` (the Task left that state
  before the change was made; a later effect shows the new one), `gave-up`, or `refused`.
- The journal is append-only, and the database enforces it: it refuses an `UPDATE` or a `DELETE` of a line. Only the loop writes it, and every
  line names the Task and the cause.

Every attempt starts by **reading the issue's labels**, and changes only what is wrong. So a crash between the change and its result costs
nothing: the first tick after the restart reads the issue, finds the label in place, records `already-in-place`, and does not change it again.

## When GitHub is unreachable

An attempt that cannot reach GitHub (no connection, a server error, a timeout) is tried again at a time that the journal records, and the
Task keeps its state meanwhile. The waits double from 30 seconds to at most 10 minutes. A rate limit is not a failure: GitHub's wait is
kept, and nothing is sent until it is over. The number of failed attempts that are allowed is the installation setting `effectRetries`
(0 to 20, default 5):

```bash
docker exec hephaestus hephaestus setting set effectRetries 8
```

A credential that is missing or unreadable (`not-connected`, `key-unreadable`) counts the same way, so mending it within those waits lets
the label be made. With the default, the waits add up to a little under 15 minutes.

At the bound, the effect is not tried again, the journal records `gave-up`, and the Task moves to **`waiting-for-owner`, not `failed`**,
in the same transaction. This raises an Escalation like any other ([`escalation.md`](escalation.md)): the intents of the new label, of a
comment that says which effect could not be completed and why, and of the notification are journaled with the move. The comment is made once
GitHub accepts writes again.

## When GitHub refuses for good

An effect that GitHub refuses for good, because the issue was deleted (`issue-not-found`), the App is not installed on the repository any
more (`app-not-installed`), or its permission was withdrawn (`rejected`), is not retried. The journal records `refused` with the reason,
and the Task moves to `waiting-for-owner`. A Task that has ended (`failed`, `cancelled`, `withdrawn`, `merged`, `closed`) stays as it is.

**In v0.1, such a Task is visible only in the log when its issue can no longer be written to.** Hephaestus has nowhere to say it: the
issue is gone, or no longer accepts its writes. The log has one error line for it, with the Task's id:

```text
The effect <key> of Task <id> was not made (refused: issue-not-found); the Task moved to waiting-for-owner.
```

A comment that says a label was ignored never moves a Task. On an issue that is no Task the line names the effect, the repository, and the
issue, and says that no Task is affected; on a Task's issue it says that the Task keeps its state.

Read the log with `docker compose logs hephaestus`. Attempts that will be tried again are one warning line per tick. A Task in
`waiting-for-owner` is resumed by an answer on the issue, which a later story adds; until then, look at the Task's issue and the App's
permissions, and fix the cause, or remove the Marker Label to cancel the Task ([`escalation.md`](escalation.md)).
