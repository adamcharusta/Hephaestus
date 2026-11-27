---
title: 'Story 3.11: Give every Agent the same instructions for the same step'
type: 'feature'
created: '2026-11-27'
status: 'done'
baseline_commit: 'cb5259cfcb75d0a4c102e86cace87879c118adf0'
route: 'dispatch'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-3-context.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** The `default` Methodology holds a manifest and no instruction template, Core assembles no context file set, no result type or `result.schema.json` exists, and nothing records which template a step applied (FR-61, FR-62, FR-64, FR-69, AD-12, AD-13, AD-14, AD-21).

**Approach:** The `default` Methodology gains `implementation.md`, a plain role prompt. A port reads a Methodology from the image; Core selects the template by step kind only and writes one context file set, whose `instruction.md` is the template's bytes and so is the same whichever Agent performs the step. Core defines the Implementation result type and generates its schema restricted to what both CLIs accept.

## Boundaries & Constraints

**Always:**
- A template is the file `<step-kind-tag>.md` in the Methodology's directory; Core selects it from the step kind alone and never reads the Methodology's name or an Agent. No Methodology named, or one that is not shipped, is the built-in `default` only when none is named; a named one that is missing is refused with a message id.
- `instruction.md` is the template's bytes unchanged. Templates have no substitution, name only paths under `/hephaestus/context` and `/workspace`, and rely on no skill, sub-agent, slash command, or repository instruction file. They tell the Agent to write prose fields in the language `task.json` names.
- The file set uses the fixed names; an input that does not exist is an absent file. Only an admitted issue snapshot and admitted comments enter a file. `task.json` carries ids, branch, start commit, Base Branch, step kind, round, and language.
- The assembled set states the Methodology's name and the template's identity (its path and SHA-256) for the step run record.
- `result.schema.json` uses only `type`, `properties`, `required`, `additionalProperties: false`, `items`, `enum`, and `description`, every property required; a test fails on any other keyword.

**Never:**
- Add a template for a kind other than `Implementation`, define the other result types, wire assembly into the loop, dispatch, or the runtime, read Project-supplied Methodology files from the Base Branch, add a step-run table, or edit the epics, the architecture, or the workflow.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Same step, two Agents | Same input assembled for each | `instruction.md` byte-identical, the whole set equal | N/A |
| No Methodology named | `null` or blank name | `default` is read and used | N/A |
| Absent inputs | No comments, plan, findings, validation, handoff | Those files are absent | N/A |
| Language | `pl` | `task.json` says `pl`; the template is unchanged | N/A |
| Missing template | Methodology has no file for the kind | Refused with `methodology.template-missing` | No file set made |
| Unknown Methodology | Named, not shipped | Refused with `methodology.unavailable` | No file set made |

</frozen-after-approval>

## Code Map

- `methodologies/default/implementation.md` (new), `manifest.yml` -- the template and its manifest text.
- `src/Hephaestus.Core/Methodologies/` (new) -- `Methodology`, `MethodologyTemplate`, `TemplateIdentity`, selection, exception.
- `src/Hephaestus.Core/Context/` (new) -- `ContextFiles`, `StepContextInput`, `AssembledContext`, `StepContextAssembler`.
- `src/Hephaestus.Core/Results/` (new) -- `StepOutcome`, `DecisionQuestion`, `ImplementationResult`, `ResultSchemas`.
- `src/Hephaestus.Core/Ports/IMethodologyCatalog.cs`, `src/Hephaestus.Host/Hosting/DirectoryMethodologyCatalog.cs` -- `ReadAsync`.
- `src/Hephaestus.Core/Localization/*` -- two message ids.
- `docs/methodologies.md`, `docs/adr/0027-*.md`, `docs/step-containers.md`, `README.md`.

## Tasks & Acceptance

**Execution:**
- [x] Template, Core records, assembler, schema, port, host catalog, messages; tests.
- [x] Documentation, ADR, story `done` in `sprint-status.yaml`.

**Acceptance Criteria:**

| Criterion | Shown by |
|-----------|----------|
| `default` holds a manifest and `implementation.md` | `DefaultMethodologyTests` |
| No Methodology named gives `default`; selection by kind only | `StepContextAssemblerTests` |
| Fixed names, absent inputs, admitted content only | `StepContextAssemblerTests` |
| Byte-identical `instruction.md` across Agents | `StepContextAssemblerTests` |
| Name and template identity stated | `StepContextAssemblerTests` |
| Schema restricted to the shared subset | `ResultSchemasTests` |
| Language in `task.json`, template language-free | `StepContextAssemblerTests`, `DefaultMethodologyTests` |
| No skill, sub-agent, slash command, or instruction file; only context paths | `DefaultMethodologyTests` |

## Implementation Notes

- Unattended run: no human gate and no open question was left; the choices are recorded here and in ADR-0027.
- Implemented directly from this spec rather than by a subagent, because the investigation was already in this session.
- Core never reads the file system: `IMethodologyCatalog.ReadAsync` returns the whole Methodology, and the host reads the directory without following links, bounded in files and bytes.
- The step run record has no table yet (Story 3.14 writes step runs), so "the record states the Methodology and the template" is `AssembledContext.MethodologyName` and `.Template` (path and SHA-256); `docs/methodologies.md` says so.
- `result.schema.json` comes from the .NET schema exporter over source-generated metadata, closed and with the enumeration's type stated in a transform; `ResultSchemas.Violations` is the test that nothing outside the shared subset appears. The CLIs themselves were not run against it here: no credential is available in this build environment.
- Reflection-based JSON is off in this solution, so `task.json`, `comments.json`, and results use source-generated contexts (`ContextJson`, `ResultJson`).
- `README.md` is unchanged: nothing an Owner installs, runs, or configures changed; `docs/methodologies.md` and ADR-0027 carry the change.
