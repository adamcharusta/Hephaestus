# User Journeys

The protagonist is **Adam**, an experienced engineer working alone on several personal projects, with a Claude subscription and an OpenAI subscription, running Hephaestus in Docker on his own machine. Requirements in `requirements.md` name the journeys they realize.

## UJ-1 — Adam queues a day of work and reviews it in the evening

1. Adam writes five issues, each with business requirements and acceptance criteria.
2. He applies the Marker Label to each. On some he adds Role Labels for the Primary Agent, the Fallback Agent, and the Effort; the rest use the Project's defaults.
3. The Project is scheduled for Monday, Wednesday, and Friday at 09:00. On Wednesday at 09:00 Hephaestus takes the first Task, prepares an isolated Workspace and a branch, runs the Primary Agent, runs the Project's build and test commands, has the other Agent review the result, lets the implementer correct blocking Findings within a bounded loop, and opens a pull request.
4. It takes the next Task. That Task builds on the first, so its branch starts from the first Task's branch. A Task that does not depend on pending work gets its own branch from the Base Branch.
5. At 22:00 Adam finds five pull requests. Each states which Agents wrote and reviewed it, what was validated, and where it sits in the Stack, with links to the previous and next pull request.

**Edge case:** the last Task is still running when the Schedule's end time passes. Hephaestus finishes it and starts nothing new.

## UJ-2 — Adam approves, and Hephaestus merges

1. Adam reads the five pull requests and approves three.
2. At the start of the Project's next scheduled window, Hephaestus merges each approved pull request, in Stack order, provided the approval came from a Trusted User, covers the latest commit, and the repository's checks pass.
3. It then continues with the Queue.

**Edge case:** the second pull request in a Stack is approved but the first is not. Hephaestus merges nothing from that Stack above the unapproved one and reports why.

## UJ-3 — Adam asks for changes in the middle of a Stack

1. On the third pull request Adam leaves review comments and issues the Rework Command. Ordinary comments are ignored; the explicit command is the trigger.
2. In the next window Hephaestus reworks the third pull request according to the comments and re-runs Validation and independent Review.
3. It then updates the fourth and fifth so they include the change.
4. Adam can repeat this as often as he wants; each round happens only on his command.

## UJ-4 — The primary agent runs out of usage

1. Midway through a Task the Primary Agent reports that its Usage Limit is reached.
2. Hephaestus records the state of the work and hands the Task to the Fallback Agent with the requirements, the plan, the current diff, and the Validation results so far.
3. The Fallback Agent inspects the partial work before continuing.
4. The Reviewer must differ from the Agent that wrote the latest changes, so the Review falls to the first Agent once its limit resets.

**Edge case:** no Agent has capacity. The Task waits and resumes automatically when a limit resets, with no action from Adam.

## UJ-5 — Hephaestus gets stuck and asks

1. An issue is ambiguous on a point that changes the implementation.
2. Hephaestus posts a specific question as a comment on the issue, marks the Task as waiting for Adam, and sends an alert by email or phone push.
3. It takes the next Task in the Queue.
4. Adam answers in a comment from his phone an hour later; Hephaestus picks the Task back up with his answer.

## UJ-6 — Adam adds a second project

Adam registers another repository, gives it its own defaults (Agents, Validation commands, Base Branch, stacking mode) and its own Schedule (Tuesday and Thursday). From then on the two Projects are worked on their own days with no change to Hephaestus itself.

## UJ-7 — Adam turns a planned epic into Tasks

1. Adam has planned a capability for one of his Projects with BMAD Method, on his own, and the resulting Stories are merged into the repository.
2. He asks Hephaestus to create issues from them. Each Story becomes an issue that links back to the Story, carries its acceptance criteria, and declares which earlier Story it depends on. Nothing starts yet.
3. Adam reads the issues, applies the Marker Label to the four he wants done this week, and leaves the rest.
4. The day runs as in UJ-1, with every Agent working to the same BMAD workflows.

**Edge case:** one Story is large. Hephaestus has the Agent plan that single Task first and includes the plan in the pull request.
