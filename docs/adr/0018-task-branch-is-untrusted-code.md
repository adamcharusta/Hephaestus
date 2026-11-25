# ADR-0018: A Task branch is untrusted code in a trusted repository

- **Status:** Accepted (added by review; the owner confirmed the default protected paths)
- **Date:** 2026-11-25
- **Spine:** AD-19

## Context

Keeping the GitHub credential out of the step container (ADR-0006) does not limit what that credential can be made to carry. The gateway pushes agent-written code to a branch in the Project's own repository, not a fork. The repository's existing automation then runs it: a workflow triggered by a push or a pull request executes agent-written build and test code with the repository's secrets and token. If the agent could also edit workflow files, it could write its own.

Separately, text an agent produces is posted under Hephaestus's identity, where it can mention people, close issues with a keyword, or imitate the hidden markers used for recovery (ADR-0007).

## Decision

- The GitHub App is granted contents, issues, and pull requests read and write, and metadata, checks, and statuses read. It is never granted `workflows` or administration.
- The gateway does not push a bundle whose diff against the Task's start commit touches a protected path. The default protected paths are `.github/**` and `hephaestus.yml`. The list is installation configuration, per Project. A hit raises an Escalation and keeps the Workspace.
- Registering a Project reports whether its Base Branch is protected against direct pushes.
- Agent-derived text is neutralised before it is posted: HTML comments are escaped, mentions and issue-closing keywords are broken, command keywords are fenced.
- A hidden marker or a pull request counts for recovery only when written by Hephaestus's own identity and matched by a journaled intent.

## Alternatives considered

- **Withhold the `workflows` permission and nothing more.** GitHub then rejects the whole push when an agent touches a workflow file, and every later checkpoint push fails with it. Refusing at the gateway gives a defined outcome instead. Rejected as insufficient.
- **Require branch protection at registration.** Branch protection is not available for private repositories on every free plan, and the product must not require a paid service. Reported, not required.
- **Push Task branches to a fork.** Workflows on forks do not receive secrets, which would remove the CI exposure. It complicates pull requests, stacks, and the App's permissions. Not adopted for v1; worth revisiting.

## Consequences

- A Task that legitimately needs to change CI or Project Configuration stops with a question to the Owner, who can lift the protection for that Project.
- **Existing workflows still run agent-written code.** Hephaestus cannot prevent this. Installation documentation must tell Owners to give workflows triggered by `hephaestus/*` branches and their pull requests a read-only token and no deployment or publishing secrets.
