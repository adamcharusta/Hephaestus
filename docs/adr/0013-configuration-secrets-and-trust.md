# ADR-0013: Configuration split, secrets, and trust admission

- **Status:** Accepted (trust by numeric id and the masker's scope were added by review; the owner confirmed moving the Base Branch into the installation and, later, how edits are detected)
- **Date:** 2026-11-25
- **Spine:** AD-15, AD-16

## Context

The Owner wants a Project's behaviour settings versioned in its repository. But an agent can edit files on its branch, and an outside contributor can open a pull request that edits them. Agents also read text from issues and comments that anyone may have written. Credentials must never reach a repository, a comment, or stored output.

## Decision

**Configuration is split along a trust line.**

- *In the repository*, as `hephaestus.yml` at the root: default agents and effort, methodology, Validation commands, stacking mode, correction bound, image. It is read only from the Base Branch, parsed in the core, and copied onto the Task when it starts. A running Task uses its copy.
- *In the installation*, in SQLite: registered Projects and their Base Branch, schedules, Trusted Users, aliases, limits, protected paths, authentication modes, notification channels. A value in the repository can never raise an installation limit.

The Base Branch is recorded at Project registration, not inside the file read from it. This departs from the requirements' glossary, which listed it under Project Configuration, to remove a circular definition.

**Secrets.** Values are files on a volume mounted only into the orchestrator. The database holds names. Values are entered through standard input. An agent invocation carries references to secrets, never values; only the Workspace runtime resolves them when it starts a container. Nothing a step container produces is ever written to the secrets volume. Hephaestus implements no sign-in flow: the Owner signs in with the vendor's tool and supplies the result.

**Masking.** One service built from every secret value, and each value's base64 and URL-encoded forms, filters agent output before it is stored, every outbound message, every log line, and every commit before it is pushed.

**Trust.** The gateway attaches the acting user's numeric GitHub id to each inbound event. One function in the core decides whether that id is a Trusted User. Everything else is recorded and changes nothing, with one exception: a merge by someone who is not trusted cancels the Task (ADR-0003). A label action by someone who is not trusted is also reported on the issue, once per user, kind of action, and issue; their comments and commands get no reply, because on a public repository a reply per account would let outsiders fill a thread. The issue title and body are admitted as vouched for by the Marker Label event; a comment only when its author and every later editor are Trusted Users. A later edit by anyone else holds the Task until a Trusted User confirms it with `/hephaestus confirm` on the issue. An edit is detected by comparing a hash of the text with the hash recorded when it was admitted, not by waiting for an edit event. The editor is read from GitHub's edit history; an edit whose editor cannot be established is treated as made by someone who is not trusted. Hephaestus's own content is never admitted.

## Alternatives considered

- **All configuration in the database.** Loses version control and review of behaviour changes, and needs a full editing surface before any dashboard exists. Rejected.
- **All configuration in the repository.** A change on a branch could alter Validation or add a Trusted User. Rejected.
- **Encrypted secret columns in SQLite.** With the key on the same disk this protects in appearance only. Rejected.
- **Trust by login name.** A login can be renamed and re-registered by someone else. Rejected in review.

## Consequences

- A change to how a Project is worked goes through the same merge gate as code.
- Once the Owner merges a change to Validation commands, those commands run in every later Workspace, as merged test code already does.
- Codex subscription sign-in, which needs a refreshed credential file written back after a run, is not supported until a safe refresh path exists.
- Reading the editor depends on GitHub's GraphQL API, whose `userContentEdits` lists the edits of an issue body or comment with their editors. Whether it does so for a GitHub App's token is to be confirmed by a test. It would be the gateway's only GraphQL query, sent over plain HTTP, since Octokit covers REST only.
- Where the editor cannot be read, the cost is an extra confirmation by the Owner, never an unvouched text reaching an agent.
