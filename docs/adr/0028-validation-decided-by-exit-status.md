# ADR-0028: Decide Validation by the exit status of each of the Project's commands, run one container each on a fresh checkout

- **Status:** Accepted
- **Date:** 2026-11-28
- **Spine:** AD-6, AD-11, AD-13, AD-18

## Context

An Agent that says it is finished proves nothing (FR-31). Work is acceptable only when the Project's own build and test commands pass, run where nothing an Agent did can interfere with them: not a hook, a configuration entry, or a fake test runner left in the tree, and not a credential that an Agent held. The architecture says that each command is its own container run on a fresh checkout, with no credential and with the Project's egress list alone, and that Validation is decided by exit status only. Until now the runtime started only Agent steps, and nothing recorded what a command came to.

## Decision

**One container for each command, started through the step path.** `ValidationCommandRequest` describes one command: its own step run, the Task branch, the bundle of the pushed tip, the image the Task recorded, the command, the limits, the deadline, and the Project's egress list. `IWorkspaceRuntime.StartValidationAsync` checks out the tip and starts the command's container with `StartStepAsync`, so everything the runtime already does for a step holds for a command too: the hardening, the egress proxy, the deadline that only the runtime enforces, the masked output file, and the one step-exited Signal. The command is `bash -c <command>` (the Project image contract requires `bash`).

**The checkout is part of the call.** A caller that had to remember to check out first could forget, and a command would run on what the previous one left. The call makes the checkout before it starts a command, and not when the command's container already exists, because a checkout beneath a running command would change its tree.

**Less than an Agent step.** `StepContainerRequest.IsValidation` mounts the Task volume and nothing else of a step: no context, no Methodology, no Agent CLI, and no secret; a request that carries one is refused. The egress list is `EgressPolicy.AllowedHosts` with no invocation hosts, the Project's list alone. The environment is the runtime's own.

**A pure decision from what the runtime saw.** `ValidationRun` holds the commands and the results recorded so far. A `ValidationCommandResult` is made from the exit status and the cause of the command's container (`StepExitCause`), the same facts the step-exited Signal carries, and from nothing else: no result type of an Agent step is accepted anywhere in it, and a test checks that. Validation passes only if every command exited with status 0. The first command that does not pass ends it: a non-zero status is `validation-failure`, a stop at the deadline is `timeout` (whatever status the stop gave), and a container killed for memory or gone is `infrastructure-error`, as AD-11 classes the runtime's own causes. The commands after it are recorded as `not-run`. The run is immutable and refuses a result of another command or one after the decision.

**The deadline of a command** is the Task duration the Project asks for within the installation's, and never more than 30 minutes (FR-19). A command is part of the Task's run, so it cannot be given more than the Task has; a default bound keeps a Project that sets nothing from running one command for the length of a Task.

**Recorded as `validation.json`.** `ValidationRun.ToJson` writes the outcome and, for each command, its position, text, status, exit status, duration in milliseconds, and the step run that keys its output file. The same bytes come from the same results. The assembler places the text it is given as `validation.json`. The output itself is not copied into the file: it is in the output store, and which part of it a Correction step is shown is a decision of the story that dispatches Corrections.

**Never checkpointed.** `StepKind` has no Validation member, so no `CheckpointRequest` can be made for a Validation run, and the next checkout makes the tree anew. Nothing that a command wrote, built, or installed is kept.

## Alternatives considered

- **A separate container path for Validation.** It would repeat the proxy, the deadline, the output, and the Signal, and the claim that a command is held to the same boundary as a step would rest on two copies of the code.
- **One container for all commands.** A command could then change what the next one sees, and one output file would hold several commands.
- **Taking an Agent's report into account when it agrees.** Nothing an Agent reported plays any part; an input that could would be a way for it to.
- **Counting a memory kill as a failure of the Project's code.** AD-11 classes it as an infrastructure error, which spends the step retry bound and not a Correction round; the exit status of a killed process says nothing of whether the code is right.

## Consequences

- The loop (Story 3.14) writes a step run for each command, records its deadline before it asks for the container, calls `StartValidationAsync`, builds a `ValidationCommandResult` from the step-exited Signal when it is applied, and starts the next command only while `ValidationRun.NextCommand` has one.
- The Correction loop (FR-34) spends a round on a `validation-failure` and not on the other two classes.
- The pull request description (Story 3.13) composes each command's result from the recorded results.
- That a command reaches only the Project's listed hosts rests on the egress proxy of its step run; `ValidationContainerTests` shows the proxy is given the Project's list alone, and the proxy's own tests show what it refuses.
