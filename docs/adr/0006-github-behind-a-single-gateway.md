# ADR-0006: GitHub as the first task provider, behind a single gateway

- **Status:** Accepted
- **Date:** 2026-11-25
- **Spine:** AD-4

## Context

GitHub is the first source of Tasks and the place where results are published. The product is GitHub-first, not GitHub-only. Agents must never be able to act on GitHub. GitHub does not let the author of a pull request approve it.

## Decision

One module, `Hephaestus.GitHub`, is the only code that talks to GitHub and the only holder of the GitHub credential.

- **Identity:** a GitHub App registered by the Owner. Pull requests are authored by the App, so the Owner can approve them.
- **Inbound:** polling with conditional requests. The poller is the gateway's only background activity and does nothing but append Signals, each carrying the acting user's numeric id from GitHub's own event data.
- **Outbound:** every write goes through one port, `ICodeHost`, with an idempotency key supplied by the loop (ADR-0007).
- **Git:** the gateway owns a bare mirror per Project and is the only code that pushes.
- **Limits in code:** no path pushes, force-pushes, or deletes a ref outside `hephaestus/*`. The only write to a Base Branch is a merge call carrying the approved commit id.
- The gateway never decides trust and never changes a Task.

The core speaks in its own terms (Task, pull request, comment, label). GitHub-specific shapes stay in the gateway.

## Alternatives considered

- **A personal access token or machine user.** Simpler to set up, but a personal token makes the Owner the author, who then cannot approve. A machine user is a second account to maintain with coarser permissions. Rejected in favour of an App.
- **Webhooks.** GitHub's recommendation, but they need a public endpoint, which a home installation does not have. Rejected for v1; polling is cheap with conditional requests.
- **Letting the agent write to the tracker through host-side tools** (Symphony's model). Contradicts the requirement that agents hold no GitHub authority. Rejected.

## Consequences

- Setting up a GitHub App is an extra installation step that documentation must cover.
- State changes are visible within one polling interval, not instantly.
- A second task provider would implement the same ports; how much of the model is GitHub-shaped will only be known when one is attempted.
