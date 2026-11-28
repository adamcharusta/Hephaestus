# ADR-0023: The checkpoint of a step: one commit made outside the volume's git directory, one bundle file read back

- **Status:** Accepted
- **Date:** 2026-11-27
- **Spine:** AD-9, AD-6, AD-18

## Context

ADR-0010 decided that a checkpoint follows every checkpointed step, however it ended, and that the checkpoint container never uses the volume's `.git`. An Agent works in a repository that it can write to, so whatever is in `/workspace/.git` (configuration, hooks, a replaced object store) is the Agent's.
Story 3.6 makes the decision true: something has to turn the work tree into one commit on the last pushed tip, with a message and an identity that Core chose, and bring it out of the container as a file that the gateway can later check (Story 3.7).

## Decision

**The checkpoint container makes its own git directory.** `CheckpointScript` runs in a container of the `hephaestus-tools` image as `10001:10001`, with no network, no credential, every capability dropped, and the Task volume as its only mount, read-only and with `volume-nocopy`. It makes a bare repository with an empty template in the container's own file system,
`/checkpoint/repository`, fetches the bundle of the last pushed tip into it (the same file the checkout is given, placed the same way, verified the same way: one head, the tip the runtime named), and sets `GIT_DIR` to it and `GIT_WORK_TREE` to `/workspace`. Git then looks for no other repository, so no configuration, hook, or attribute driver in `/workspace/.git` is read or run,
and the volume is never written to.

**One commit, over the tip's tree.** The index is made from the tip's tree (`git read-tree`), then `git add --all` takes the work tree as it is, `.gitignore` respected, so a tracked file stays tracked and an ignored one is not committed. A repository an Agent made inside the workspace (a `.git` below the top) is not committed as a link to a commit: its entry is removed from the index, and its files are not captured.
A tree equal to the tip's ends the script with exit code 30 before a commit exists; the commit is `git commit-tree` with the tip as its one parent, so whatever the Agent committed is flattened away. The message and identity are Core's: `CheckpointCommit` composes a subject from a message id in the Task's language and the trailers
`Hephaestus-Task`, `Hephaestus-Step`, `Hephaestus-Agent`, `Hephaestus-Model`, and `Hephaestus-Effort`, and `CommitIdentity` names the App's bot user; both reach the script in its environment and never in its text. `git bundle create` then writes one head, `refs/heads/<Task branch>`, with the tip as its one prerequisite.

**One file comes back, and only that.** The runtime copies `/checkpoint/result.bundle` out of the stopped container with Docker's archive call and reads the archive with a tar reader: the first entry must be a regular file called exactly that, no larger than the bound (the same 2 GiB as the gateway's), and there must be no second entry.
A link, a directory, a path with a parent or a prefix, another name, an extra entry, a size over the bound, or a stream that is not a tar is an infrastructure error (`WorkspaceRuntimeException`, a message id of its own in `en` and `pl`), and the file that was started is deleted. The file is written under a temporary name in the bundles directory, its SHA-256 is taken while it is copied,
and its header is read as text by `BundleHeader`, because the orchestrator runs no git on what came out of a container: it must have one head on the Task branch and one prerequisite, the tip it was asked for, and its commit must not be that tip. Only then is it moved to `<task>/<step-run>-checkpoint.bundle` and returned as a
`BundleReference` with its path, checksum, tip, and prerequisite. The loop deletes it once the push is journaled (Story 3.7). A container that ended well and left no file is an error, never an unchanged tree: unchanged is the exit code, not the absence of a file.

**Only the kinds that change code are checkpointed.** `StepKind.IsCheckpointed` is true for `Implementation`, `Correction`, and `Rework`; a `CheckpointRequest` for any other kind cannot be made, and what such a step left in the tree is discarded by the next checkout, which empties `/workspace`.

## Alternatives considered

- **Commit in the volume's own repository with its hooks and configuration switched off by flags.** The list of what to switch off is the attacker's to outgrow; a repository that was never read has nothing to switch off (AR-50).
- **Mount the volume read-write so that the container can `git add` as it likes.** Nothing it does needs a write; read-only removes a class of mistakes, and the index lives in the git directory.
- **Parse the bundle with git in the orchestrator for its tip.** Runs git on the output of an Agent's container, which ADR-0010 forbids; the header is plain text and the gateway checks the bundle with git, in quarantine, before any ref moves.
- **Print the tip from the container.** A second channel for the same fact, and one that an Agent's tree cannot be kept out of by construction; the header is part of the one file that is already bounded and checksummed.

## Consequences

- A nested repository inside the workspace is not captured; a submodule the tip tracks keeps the tip's entry, and nothing else about submodules is handled (`deferred-work.md`). A file the step user cannot read is left out, and the rest is kept.
- The Agent's own `.gitignore` decides which new files are committed, as it does for anyone who runs `git add -A`: a `.gitignore` of everything keeps tracked files and their edits and drops new ones, and Validation on the pushed commit fails if a dropped file mattered.
- The commit's address is `<slug>[bot]@users.noreply.github.com`, without the user id that GitHub uses to link the avatar, because the App's slug is what GitHub reports and nothing here asks for the bot user's id. Attribution is read from the step run's records, never from a commit.
- The checkpoint reads the whole work tree once, so it has a limit of its own (`CheckpointLimit`, 15 minutes), and a container that does not end in time is removed by force; the work is still in the volume, which is kept.
- The loop calls the checkpoint when a checkpointed step has ended (Story 3.14, ADR-0030). The runtime refuses a checkpoint while a container of the step run still runs on the volume, and never stops one (only the step watcher does, AD-18); a caller asks for a checkpoint after the step-exited Signal.
