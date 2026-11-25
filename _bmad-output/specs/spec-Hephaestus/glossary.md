# Glossary

Terms are capitalized wherever the spec uses them in this sense. They are also the type and member names in code (architecture spine, Consistency Conventions).

## People and trust

- **Owner** — The human who operates a Hephaestus installation and makes all approval decisions. One per installation in v1.
- **Trusted User** — A GitHub user whose label, approval, answer, or command Hephaestus acts on. Actions by anyone else are ignored.

## Projects and configuration

- **Project** — A GitHub repository registered with Hephaestus. An installation has one or more.
- **Project Configuration** — The version-controlled file in a Project's repository that defines how Hephaestus works on it: default Agents and Effort, Methodology, Validation commands, stacking mode, Correction Loop bound, and execution environment. Read only from the Base Branch.
- **Installation Configuration** — Settings held by the installation and never stored in a repository: registered Projects with their Base Branch and Schedules, Trusted Users, Agent credentials, Aliases, limits, protected paths, and Notification channels.
- **Base Branch** — The Project branch that independent Tasks branch from and that approved pull requests are ultimately merged into. Set when the Project is registered.
- **Schedule** — A Project's working time: days of the week, a start time, and an optional end time. The end time stops new Tasks from starting; it does not interrupt a running one.

## Tasks and labels

- **Task** — A GitHub issue in a Project that carries the Marker Label applied by a Trusted User. One issue is one Task.
- **Marker Label** — The label that opts an issue in to Hephaestus. The only mandatory label on a Task.
- **Role Label** — A label of the form `role:alias` that assigns an Alias to a role on a Task, for example `primary:opus5.5` or `fallback:sol6.2`.
- **Priority Label** — A label that moves a Task ahead of others in the Queue.
- **Alias** — A name defined in configuration that resolves to an Agent with a Model, or to an Effort. Aliases let the Owner choose per Task without Hephaestus knowing model names in advance.
- **Queue** — The ordered set of a Project's Tasks that are ready to be worked on.
- **Readiness Check** — The assessment, made before implementation starts, of whether a Task's issue is clear enough to implement without asking the Owner.
- **Story** — A unit of planned work described in a Project's Methodology documents; in BMAD Method, a story within an epic. Hephaestus can create one issue per Story.

## Agents

- **Agent** — A coding agent that Hephaestus drives through its officially supported non-interactive interface (in v1: Claude Code and Codex CLI). An Agent may start its own sub-agents; Hephaestus does not manage those.
- **Model** — The underlying language model an Agent is asked to use. Distinct from the Agent.
- **Effort** — The reasoning effort requested from the Model for a Task.
- **Primary Agent** — The Agent that starts work on a Task.
- **Fallback Agent** — The Agent that takes over a Task when the Primary Agent cannot continue.
- **Reviewer** — The Agent that performs the Review of a Task. Always a different Agent than the one that wrote the latest changes.
- **Usage Limit** — A provider-imposed cap on an Agent's use that, once reached, prevents further work until it resets.
- **Handoff** — The transfer of a Task in progress from one Agent to another, carried by durable artifacts and repository state, not by conversation history.
- **Methodology** — A named set of instructions that defines how an Agent performs each step of a Task: Readiness Check, implementation, Review, correction, and Rework. A Project uses one Methodology, and every Agent working on it receives the same one. BMAD Method is the first supported Methodology besides the built-in default.

## Execution and quality

- **Workspace** — The isolated environment in which a single Task is worked on: a container with its own checkout of the Project on the Task's branch.
- **Validation** — The deterministic commands a Project configures (for example build and test) whose results decide whether work is acceptable. An Agent's own claim of completion is never Validation.
- **Review** — The Reviewer's independent assessment of a Task's changes, producing Findings.
- **Finding** — A single issue raised by a Review, classified as blocking or non-blocking.
- **Correction Loop** — The automatic, bounded cycle in which the implementing Agent resolves blocking Findings and Validation and Review run again.

## Pull requests

- **Stack** — An ordered chain of Tasks in which each Task's branch starts from the previous Task's branch. A Task that does not depend on pending work is not part of a Stack.
- **Rework** — A revision of an open pull request made in response to the Owner's review comments. Starts only on a Rework Command and has no round limit.
- **Rework Command** — The explicit command a Trusted User places on a pull request to start a Rework: a comment whose first line is `/hephaestus rework`.

## Communication

- **Escalation** — Hephaestus asking the Owner a question in an issue or pull request comment because it cannot proceed. The Task waits for the answer while other Tasks continue.
- **Notification** — An alert (email or phone push) telling the Owner that an Escalation or another event needs attention. A Notification never carries the conversation; answers are given on GitHub.

## Dashboard

- **Dashboard** — The web interface through which the Owner follows Hephaestus in a browser. It shows; it changes nothing.
- **Dashboard Token** — A credential the Owner issues and revokes from the command line. A browser must present it to read anything from the Dashboard.
