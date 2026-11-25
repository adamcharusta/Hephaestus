# Digest — competitive scan and GitHub label events (round 1)

Accessed 2026-11-25 through search summaries.

Sources: S22 openai/symphony `SPEC.md`, https://github.com/openai/symphony/blob/main/SPEC.md (primary, read via summary) and write-ups (verdent.ai, deepwiki, agentpatterns.ai); S23 github/gh-aw issues and third-party evaluation issues (carrick-sh/carrick #644, Oct 2026); S24 winder.ai and kilo.ai comparisons (OpenHands); S25 GitHub Docs, "REST API endpoints for timeline events", https://docs.github.com/en/rest/issues/timeline.

| # | Claim | Source | Class | Confidence |
|---|---|---|---|---|
| 58 | Symphony is an OpenAI-published open specification (Apache 2.0, April 2026) with an Elixir reference implementation: it polls a tracker, gives each issue an isolated workspace at a deterministic path, and launches a coding agent session there. | S22 | landscape | medium |
| 59 | In Symphony, tracker mutations (state changes, comments, PR links) are performed by the coding agent; the orchestrator is scheduler, runner and reader. Workflow policy lives in a version-controlled `WORKFLOW.md`. Workspaces have lifecycle hooks (after_create, before_run, after_run, before_remove); retries back off from 10 s to a 300 s ceiling. | S22 | landscape | medium |
| 60 | Sources disagree on Symphony's adapters: launch coverage says Linear + Codex only; a later guide lists GitHub Issues, Jira, Asana, GitLab. | S22 | landscape | low |
| 61 | GitHub Agentic Workflows (gh-aw) compiles Markdown workflows into GitHub Actions, with built-in engines including Copilot, Claude Code and Codex; described as a technical preview in October 2026 (v0.89.x). Stock engines accept API keys or workload identity, not personal Claude/Codex subscription logins. | S23 | landscape | low |
| 62 | OpenHands is a self-hostable (MIT) agent that works issue-to-PR in a container with any model; noted weaknesses are looping on ambiguous problems and Docker-in-Docker friction. It is itself the coding agent, not a layer over other vendors' CLIs. | S24 | landscape | low |
| 63 | `GET /repos/{owner}/{repo}/issues/{issue_number}/timeline` returns `labeled` events with an `actor` object (login, id) and the `label`; this identifies who applied a label. | S25 | integration | medium |

Not found: a self-hosted orchestrator that combines subscription-authenticated vendor CLIs, cross-vendor review, usage-limit handoff, and orchestrator-owned GitHub writes. Absence in one scan is weak evidence; small projects exist (several appeared incidentally as GitHub issue trackers discussing `rate_limit_event`).
