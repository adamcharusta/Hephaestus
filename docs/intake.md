# How an issue becomes a Task

A Task is born when a Trusted User applies the Marker Label, `hephaestus`, to an issue of a registered Project. Nobody else can make
Hephaestus work on your repository: a label from anyone else creates no Task, and neither does an issue that merely carries the label.

## Intent is read from events

Hephaestus never looks at the labels an issue has now to decide what you want. It reads **events**, each with the user GitHub says
caused it (see [`polling.md`](polling.md)), and the orchestrator loop applies them one at a time, in the order they reached the inbox.
An issue that carries the Marker Label with no labelled event behind it from a Trusted User has no Task: for example a label applied
before the Project was registered, or one applied by someone you do not trust and left there.

## What the loop does with a Signal

The loop is the only code that writes Task state. For each Signal it has not applied it makes one decision and one transaction, in which
the Signal is marked and the Task it causes is written together:

| The Signal | What happens | The Signal is |
| --- | --- | --- |
| The Marker Label applied by a Trusted User to an issue that is not a Task | a Task is created in `queued`, with the issue's title and body snapshotted and its branch derived, and the intent to show `hephaestus:queued` on the issue is journaled with it | applied, and names the Task |
| The Marker Label applied by a Trusted User to an issue that is already a Task | nothing; a Task is one Project and one issue for life, and no second Task is made | applied, and names the Task |
| The Marker Label applied by a user who is not a Trusted User | nothing | rejected: `actor-not-trusted` |
| The Marker Label applied with no actor in GitHub's data, for example by a deleted account | nothing | rejected: `actor-unknown` |
| The Marker Label applied by Hephaestus's own GitHub App | nothing; its own content is never admitted, and its bot account is never trusted | rejected: `own-identity` |
| The Marker Label on a repository that is not a registered Project, or was removed | nothing | rejected: `project-not-registered` |
| The Marker Label with no issue title in it (a Signal that carries no content for the event to vouch for) | nothing | rejected: `snapshot-missing` |
| A state label (`hephaestus:<state>`) applied or removed on an issue that is a Task, by anyone but Hephaestus's own App | the issue is made to show the Task's state again (see [`state-labels.md`](state-labels.md)) | applied, and names the Task |
| A state label on an issue that is not a Task, or applied or removed by Hephaestus's own App | nothing | applied, with no change |
| Any other label, a removed label, a close, a reopening, a comment | nothing yet; later stories act on them | applied, with no change |
| A Signal that has failed to apply at least three times over five minutes | it is set aside, so that the Signals behind it are not held back; each failure is in the log | rejected: `apply-failed` |

A Signal that cannot be applied, for example because the database is unavailable, stays pending and is tried again on the next tick, so a
short outage loses no opt-in; only one that keeps failing for five minutes is set aside.

A rejection records the acting user's numeric id (the Signal already holds it) and the reason, and the host logs each one as a warning
with both. Whether a user is trusted is decided by GitHub's numeric id alone, by one function; a login that once belonged to a
Trusted User proves nothing. Hephaestus's own bot account is recognised by the App's slug, which `github connect` stores beside its id;
`github check` stores it for an App connected before that was kept, and brings it up to date if GitHub has changed it. The bot account can never be added as a Trusted User in
any case.

A Signal that was appended and not yet applied when the host stopped is applied by the first tick after it starts again.

## The snapshot

The Task keeps the issue's title and body as they were when the Marker Label was applied, with a SHA-256 hash of the two. Later stories
build an Agent's context from this snapshot only, and compare the hash with the issue as it is then to notice an edit.

The snapshot is the title and body that GitHub's event data showed **when the poller read the labelled event**, which is within one
polling cycle of the label being applied. An edit made in between, by anyone, is therefore in the snapshot. Detecting that edit and
holding the Task for confirmation is the job of the story that handles edits.

## The branch

The branch name is derived once, when the Task is created, and stored: `hephaestus/<issue-number>-<slug>`. The slug is the title in
lower case, with every run of characters other than ASCII letters and digits turned into one hyphen, and cut to at most 40 characters
(the Polish letters `ąćęłńóśźż` are read as `acelnoszz`). A title that leaves no letter or digit gives `hephaestus/<issue-number>`.
`Add a login page` on issue 12 gives `hephaestus/12-add-a-login-page`. Renaming the issue later does not change the branch.

## Health

The loop ticks about once a second when nothing is pending. It beats after every Signal it applies and at the end of every tick that met
no failure, so a loop that fails on every tick is not reported alive. The API reports whether it has beat in the last 30 seconds, and
when it last did:

```bash
docker exec hephaestus hephaestus health
# Loop: alive, last tick 2026-11-27T12:00:03Z.
docker exec hephaestus hephaestus health --json
# {"loop":{"alive":true,"lastTickAt":"2026-11-27T12:00:03Z"}}
```

A loop that is not alive is exit code 1 (as is a host that cannot be reached; the sentence tells them apart), and it says where to look: the container log (`docker compose logs hephaestus`). A
host that has not yet ticked since it started reports `alive: false` and `lastTickAt: null`. The container's own health check still
only asks that the API answers (`version`).

## What is not there yet

The Marker Label opts in an issue that is already closed, too: the loop does not look at whether an issue is open. How a close and a
Task relate is for the story that handles closes. A Task you create is `queued`, and its state is shown on its issue as the label
`hephaestus:queued` ([`state-labels.md`](state-labels.md)); nothing more happens to it: nothing dispatches it, withdraws it when you
remove the label, or says on the issue that a label was ignored. Those are the next stories of the epic. Until the Dashboard shows
Tasks, the label on the issue and the database on the `hephaestus-data` volume are where you see them.
