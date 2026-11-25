# ADR-0003: Source of truth split between GitHub and SQLite

- **Status:** Accepted (reading intent from events only was added by review; the owner later decided how a merge by someone who is not trusted is handled)
- **Date:** 2026-11-25
- **Spine:** AD-2

## Context

A Task's state exists in Hephaestus's database and on GitHub as labels, comments, branches, and pull requests. A person can change the GitHub side by hand at any time, including while Hephaestus is stopped. Without a rule, one builder will treat the database as a cache of GitHub and another will treat GitHub as a view of the database.

## Decision

Authority follows who owns the fact.

- **GitHub is authoritative for human intent:** opt-in, approval, answers, commands, closing. Intent is read only as events that name the acting user. It is never inferred from the current label set or from whether something is open or closed.
- **SQLite is authoritative for execution state:** current step, attempts, the journal.
- State labels and comments written by Hephaestus are a one-way projection of the database and are written back if changed by hand.
- A Task is one Project and one issue number for life. Opting in again after a cancellation or failure reuses the Task, its branch, and its pull request.
- **A merge is read the same way, by who made it.** Merged by Hephaestus after an approval, or by a Trusted User, the Task becomes `Merged`. Merged by anyone else, the Task becomes `Cancelled` and the reason is reported on the issue. Hephaestus never reverts the merge. A Trusted User can accept it afterwards with `/hephaestus confirm` on the pull request, which moves the Task to `Merged`.

## Alternatives considered

- **The database always wins.** Ignores the Owner's manual actions on GitHub, which are the primary interface. Rejected.
- **GitHub always wins** (Symphony's model). A step cannot be resumed safely after a restart from tracker state alone, and FR-21 forbids duplicates. Rejected.
- **Reading intent from present state** (is the label there now?). An untrusted collaborator could withdraw a Task by removing a label, and a cancelled Task would be queued again because its label is still present. Rejected in review.
- **Treating any merge as `Merged`.** Code nobody trusted approved would count as finished work, and the Tasks that depend on it would start. Rejected.
- **Ignoring a merge by someone who is not trusted.** The Task would stay open against a pull request that no longer is, and nothing would tell the Owner. Rejected.

## Consequences

- Trust can be decided per event, by actor (ADR-0013).
- The poller must not miss events: it appends first and advances its cursor second.
- Removing a state label by hand has no lasting effect, which may surprise; documentation must say that the Marker Label, not state labels, is the control.
- Cancelling the Task does not remove the merged code: it is on the Base Branch, and later Tasks branch from it. Branch protection is what prevents such a merge; Hephaestus only refuses to treat it as approved work. Tasks that depend on the cancelled one wait until a Trusted User confirms the merge.
- A collaborator with write access who is not a Trusted User can cancel a Task by merging its pull request. This is the one case where an action by someone who is not trusted changes a Task.
