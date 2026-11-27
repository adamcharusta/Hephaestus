# How Hephaestus notices what happens on GitHub

Hephaestus polls GitHub. It has no public endpoint, takes no webhook, and needs no port open on your router, so an installation behind a
home router works as it is. What is noticed is recorded as Signals; the orchestrator loop applies them ([`intake.md`](intake.md)), and so far
only a Trusted User's Marker Label does anything: it creates a Task.

## What is watched

Each registered Project, and no other repository, is asked about two things on every polling cycle:

| What | GitHub's data | Becomes a Signal of the kind |
| --- | --- | --- |
| An issue is labelled, unlabelled, closed, or opened again | the repository's issue events | `labeled`, `unlabeled`, `closed`, `reopened` |
| A comment is created or edited | the repository's issue comments | `comment-created`, `comment-edited` |

Events about pull requests are skipped for now, and so are all other issue events (assigning, renaming, and so on). A comment on a pull
request's conversation is an issue comment to GitHub and is recorded like any other, under the pull request's number; the Signal does not
say which of the two it is. A Project that was removed is not read, and a repository that is not registered never is. A Project is watched from the moment it was registered; what
happened on the repository before, or while the Project was removed, is not read when it is registered again.

## What a Signal holds

A Signal is one row of the persisted inbox: where it came from (`github`), its id at that source, its kind, the repository's GitHub id,
the issue number, the label or comment it concerns, when GitHub says it happened, and who acted.

- **Who acted** is GitHub's numeric user id, taken from GitHub's own event data and never from what an issue or comment says. The login is
  kept for display only. Whether that id is a Trusted User is decided later, in one place, and not by the poller.
- **A labelled event carries the issue's title and body** as GitHub's event data showed them when the poller read it, because the Task that
  the label may create is snapshotted from them ([`intake.md`](intake.md)). No other kind of Signal carries content.
- **A comment edit has no actor.** GitHub's comment data names the author, not the person who edited, so an edit's actor is recorded as
  unknown and the author is kept apart. An actor that is unknown is never a Trusted User.
- **A comment that was edited before it was first seen** still gives its creation Signal (when it was made after the Project was
  registered) and then the edit.
- **GitHub numbers its issue events in the order they happened**, and the cursor relies on it: it is the newest event id read, and a
  cycle reads pages back to that id. An event that GitHub were to show late, with an id below one already read, would not be seen.
  Whether GitHub ever does so has not been checked against GitHub itself.
- **Signals are in the inbox in the order they were appended**; the database numbers them. Within one cycle the issue events of a Project
  come first, oldest first, and then its comments, oldest update first, so the order across the two is not the order on GitHub.

## One Signal for one event

The inbox refuses a second Signal with the same source and source event id, without an error. An event GitHub shows again is therefore
appended once. The poller appends first and moves its cursor second: if Hephaestus stops between the two, the next cycle reads the same
events again, the inbox refuses the ones it holds, and the cursor then moves. Nothing is lost and nothing is doubled.

## Conditional requests, minimum intervals, and rate limits

- Every request after the first carries the entity tag of the last answer. When nothing changed, GitHub answers `304 Not Modified`,
  which does not count against the rate limit, and the cursor is left as it was.
- When GitHub states a minimum time between requests for a repository (`X-Poll-Interval`), that repository is not asked again before it.
- When GitHub answers that a rate limit is reached (a `429`, or a `403` that says so), Hephaestus sends **no request at all**, to any
  endpoint, until the time GitHub states, and then goes on. The wait is treated as GitHub being unavailable: it is logged, no cursor
  changes, and nothing is lost. A wait is never taken to be longer than two hours.
- When GitHub cannot be reached, the cycle ends with every cursor as it was, and the next cycle tries again.

## The polling interval

The polling interval is the installation setting `pollingInterval`, in whole seconds, from 10 to 3600. The default is 60.

```bash
docker exec hephaestus hephaestus setting list
docker exec hephaestus hephaestus setting set pollingInterval 120
```

A change takes effect without a restart: the setting is read again at least every 15 seconds, also in the middle of a wait. **This is the polling cycle** that the
requirements for noticing a change (FR-11) and for acting on an answer (FR-29) measure against: a change on GitHub is noticed within one
cycle, plus the time GitHub asks to wait, if it asks. A shorter interval makes Hephaestus quicker to notice and uses more of the rate
limit when something changes; a request that finds nothing new costs none.

## What the poller does not do

It appends Signals and keeps its cursors, and nothing else: it decides no trust, changes no Task, and writes nothing to GitHub. The
only requests it sends are reads of the repository's lists, authenticated with an installation token of the GitHub App. It follows a
link to the next page only when the link names `api.github.com`.
