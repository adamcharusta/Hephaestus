# ADR-0027: Write one instruction file per step run from the Methodology's template for the step kind, and nothing that depends on the Agent

- **Status:** Accepted
- **Date:** 2026-11-27
- **Spine:** AD-12, AD-13, AD-14, AD-21

## Context

Moving a Task between Agents, or having one Agent review what the other wrote, must change who does the work and not how it is done (FR-62). The architecture says that Core selects a template by step kind only, that templates have no substitution, that the adapter's prompt is one fixed sentence pointing at `instruction.md`, and that a step run records the Methodology and the template it applied. Until now the `default` Methodology held a manifest and no template, nothing assembled the context files, and no result type existed to generate `result.schema.json` from.

## Decision

**A template is a file named after its step kind.** A Methodology is a directory; its template for a kind is `<kind>.md`, with the kind's tag (`implementation.md`). `MethodologyTemplates.Select` takes the Methodology's files and the kind and nothing else: it cannot read the Methodology's name, because it is not given it, and it cannot read an Agent, because there is none in its input. A Methodology supports the kinds it has a file for; one without a file for the kind of the step is refused with `methodology.template-missing`. This story adds `Implementation` to `default` and no other kind (later stories add the others).

**`instruction.md` is the template's bytes.** Nothing is added, so no substitution is possible and the file is the same whichever Agent performs the step, in whichever language, for whichever Task. What is particular to the Task is in the other files, which the template names by path. The language is one of them: `task.json` carries it, and the template tells the Agent to write the prose of its result in the language that file names, which keeps one set of templates for every language (AD-21).

**One assembler writes the set.** `StepContextAssembler.AssembleAsync` reads the Methodology through `IMethodologyCatalog.ReadAsync`, selects the template, and writes `instruction.md`, `task.json`, and `result.schema.json`, and `issue.md`, `comments.json`, `plan.md`, `findings.json`, `validation.json`, and `handoff.md` when their input exists; an input that does not exist is an absent file. Its input names no Agent, so assembling the same step for two Agents is one call whose result both adapters use. The issue and the comments are typed as the admitted snapshot and the admitted comments (AD-15), so that content that was not admitted has no way in; `comments.json` leaves out the hash and the Signal that vouched for a comment, which an Agent has no use for. Output is the same bytes for the same input: no time is written, and keys and files have a fixed order.

**No Methodology named is `default`.** A Project that selects none, and has no Methodology files, gets the built-in one (FR-61). A name that is not shipped is refused with `methodology.unavailable`, not replaced: Project Configuration checking already refuses such a name, so reaching this point means the image changed, and a different Methodology's instructions would be a silent change of how the work is done.

**The record states what was applied.** `AssembledContext` carries the Methodology's name and a `TemplateIdentity`: the template's path and the SHA-256 of its bytes, so that an edited template is another identity. The step run's record holds both when the loop writes step runs (Story 3.14 on); until then no table has a column for them.

**The result type is a Core record and its schema is generated.** `ImplementationResult` (outcome `completed` or `needs-decision`, a summary, and the questions that `needs-decision` carries) serves `Implementation`, `Correction`, and `Rework`. `ResultSchemas.For` generates its schema with the .NET schema exporter from the source-generated metadata, then closes every object (all properties required, no others) and states the type of an enumeration. `ResultSchemas.Violations` finds any keyword outside `type`, `properties`, `required`, `additionalProperties`, `items`, `enum`, and `description`, and a test runs it on the generated schema, so that a result type that needs a reference, an alternative, a nullable type, or a size constraint fails the build and not a step in production. That subset is what both CLIs accepted in Story 1.1 (`docs/spikes/injection`). A result is read with `ResultJson`, which refuses a key the record does not have. Types for the other kinds are added with the stories that use them.

**The Methodology reaches the runtime as files.** `DirectoryMethodologyCatalog.ReadAsync` reads a shipped Methodology's whole directory, in ordinal order, without following a link, and bounded in files and bytes, because what it reads is placed in a volume for every step. Reading the files a Project supplies from the Base Branch (AD-14) is a later story.

## Alternatives considered

- **A template with placeholders for the Task's data.** The two `instruction.md` files could differ, a template could be used to carry untrusted text into the instruction, and a placeholder syntax would be a feature of every Methodology. Rejected by AD-14; the data is in files the template names.
- **A template per Agent, or per language.** The same standard for every Agent is the point; a second set per language doubles every Methodology (AD-21).
- **A manifest that lists the templates.** A second place that can disagree with the directory. The file name is the list.
- **Descriptions in the schema.** The exporter does not read them from the source-generated metadata, and the template already explains each field.

## Consequences

- Adding a Methodology, or a kind to one, adds files and no code.
- The loop (Story 3.14) calls the assembler, hands `Context` and `Methodology` to the runtime, and records the Methodology's name and the template's identity on the step run; the adapter's `ResultSchema` is the same text as `result.schema.json`.
- Whether a template makes both CLIs do the work well is shown by running them, with the Owner's credentials, and is not claimed here.
