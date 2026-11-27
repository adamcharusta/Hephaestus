# ADR-0024: Push a Task branch only through the gateway's checks, as a journaled fast-forward

- **Status:** Accepted
- **Date:** 2026-11-27
- **Spine:** AD-4, AD-5, AD-9, AD-19

## Context

A checkpoint (ADR-0023) leaves one bundle file on the data volume. The bundle comes from a container that an Agent's code ran in, so nothing in it is trusted: not its refs, not its objects, not its history, not the files it adds. Story 3.7 makes the gateway the only code that moves a Task branch on GitHub, and makes every
way a push can go wrong a refusal that the Owner is told about.

## Decision

**One port, one implementation.** `ITaskBranches` has three members: `CheckBranchAsync`, `PushAsync`, and `DeleteBundle`. `GitHubMirrors` implements it beside `IRepositoryMirrors`, with the same runner (`GitProcess`), the same per-repository gate, and the same token handling: the installation token is put in the environment of the
one git process that talks to GitHub, as a header for GitHub's address, and nowhere else. A request cannot name a ref that is not a Task branch (`PushRequest` takes only `BranchName.IsTaskBranch`), and the push has one refspec, from the quarantine ref to `refs/heads/<Task branch>`: no plus, no `--force`, no `--delete`, no `--mirror`. A push that is not a fast-forward is refused
before git is asked, and git refuses it again on GitHub's side.

**The checks, in order, and the first failure blocks.** The gateway fetches the mirror, reads the Task branch, and then:
1. *Already pushed.* A branch at the bundle's tip, or beyond it, means an earlier attempt was made and not recorded (AD-5): the result is `AlreadyPushed` and nothing is pushed, so a push is never made twice.
2. *The bundle's file* is a regular file under the bundles directory, not a link, no larger than the bound, with the SHA-256 the reference carries (`import`).
3. *The branch* is where Hephaestus left it: at the last pushed tip, or absent when nothing was pushed, which is when the last pushed tip is the Task's start commit. Any other tip means someone else pushed or deleted: `remote-moved`, with the commits Hephaestus did not push (at most ten, as abbreviated ids).
4. *The bundle's heads* are exactly one, `refs/heads/<Task branch>`, at the tip it was said to carry (`branch`, `import`).
5. *The import* is `git fetch` of the file into `refs/hephaestus/quarantine/<step run>` with `transfer.fsckObjects` and `fetch.fsckObjects` on and only that ref named, so nothing else moves (`import`). The one command that reads a file of the data volume is given the `file` protocol, because git counts a local bundle as one; nothing else is.
6. *Fast-forward*: the last pushed tip is an ancestor of the new tip, and the new tip is not the last pushed tip (`fast-forward`).
7. *Secrets*: every new commit, tree, and blob, which are the objects reachable from the tip and not from the last pushed tip, is read with `git cat-file --batch` and scanned as it streams with the masker's own forms (plain, base64, URL-encoded). A commit carries the message, a tree the names, a blob the content. A scan that did not read every object is a failure and not a pass (`secret`).
8. *Protected paths*: the paths of `git diff-tree -r --no-renames <start commit> <tip>`, so a rename is both its paths and a deletion is the path deleted, are matched against the Installation Configuration's list (`protected-path`).

The quarantine ref is removed whatever came of the push, and the mirror's own `refs/heads/<Task branch>` is moved to the tip only after GitHub accepted the push.

**The protected path matcher is case-insensitive and anchored.** A glob is matched against the whole path from the repository's root; `**` is zero or more segments, `*` and `?` stay inside one. Matching ignores case because repositories are checked out on file systems that do, where `Hephaestus.yml` is the file `hephaestus.yml`; the cost is
that a path under `.GitHub/` is refused too, on a file system where it is not the same directory, which is the safe side to be wrong on. This settles the open question that Story 1.9 recorded.

**A push is a journaled effect, and its intent is the audit record of the tips.** `EffectKind.Push` is journaled with the state change that causes it (the end of a step run, Story 3.14). Its cause is `s` and the first 31 digits of the step run's id, and its detail is `push|<step run>|<sha256>|<tip>|<last pushed tip>|<start commit>`, which is the whole intent: the bundle's path
is derived from the Task and the step run, and the tip before and after are in the journal, which is append-only, so the step run's record of them is the journal until Story 3.14 gives the step run a record of its own. The detail column is 256 characters for this. After a crash the push is resolved by reading the branch, as a fast-forward, not by a marker (AD-5).
The loop deletes the bundle's file once the result of the effect is journaled, whatever it is, and keeps it while the push is being retried.

**A failed check ends the effect as refused and raises an Escalation of its own kind.** `EscalationKind.PushBlocked` carries the check (`import`, `branch`, `remote-moved`, `fast-forward`, `secret`, `protected-path`) and what it found as facts, and the comment names the check by its tag, which is never translated. The Task moves to `WaitingForOwner` through the one path, and the Workspace is not touched: nothing in the loop removes a volume, so it is kept.
A branch that moved is the one case that a reply does not end: its resume condition is `confirm`, which Story 3.18 delivers, because a reply that returned the Task to the Queue would run a step on a tip nobody accepted.

## Alternatives considered

- **`git bundle unbundle`.** Writes the objects and takes no refspec, so it cannot be told to accept the Task branch into a name of ours and nothing else; a fetch into a quarantine ref can, and is what the architecture names.
- **`git push --force-with-lease`.** A lease is a force with a condition. Nothing here needs a force, and a code path that has one is what the story says must not exist.
- **Scanning with `git grep`.** It searches a tree, not "what is new", and it reads blobs by pattern; the masker's forms are not patterns git knows.
- **A step run table now.** The record of tips before and after belongs to the step run, which Story 3.14 creates with the dispatch; a table made here would be a second design for it. The journal's intent says the same thing, and the table can be filled from it.
- **Matching protected paths as case-sensitive, as git does.** The default entries would then not protect `Hephaestus.yml` on a Mac or a Windows machine that reads the repository.

## Consequences

- The diff for the protected paths is the net change from the start commit to the tip, as the story asks. A checkpoint is one commit, so no commit inside a bundle can add a protected file that a later one removes; a bundle from anywhere else is not what this gateway is asked to push. Aliases of a path that only some file systems know (NTFS short names, trailing dots) are not matched.
- The branch is read before the bundle's file is checked, so that a push that was made and whose file is gone, or is not what it was, is resolved as already pushed and not reported as a bad bundle; nothing is pushed in either case.
- A blocked push on a Task that already waits for the Owner (an Agent asked a question in the step that was checkpointed) raises no second Escalation, because one Escalation asks for one decision: the effect ends as refused with the check as its reason, and the journal and the log say which. `deferred-work.md` records the question of how the Owner should be told.
- The scan finds only what the masker's forms find: a secret split across lines, compressed, or in UTF-16 text is not caught (T16 in the threat model says so).
- A Task whose branch was moved by someone else stays held until Story 3.18's `confirm`; until then the Owner's way out is to cancel the Task.
- Nothing calls the push yet. The loop makes a `Push` effect that has been journaled; journaling it at the end of a step run, calling `CheckBranchAsync` before a step, and recording the Task's start commit are Story 3.14's.
