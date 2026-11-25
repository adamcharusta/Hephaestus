# ADR-0019: Language is data; the machine surface has one language

- **Status:** Accepted (owner decision)
- **Date:** 2026-11-25
- **Spine:** AD-21

## Context

The owner wants English by default and Polish as a choice, on everything the Owner reads: the Dashboard, the text Hephaestus writes on GitHub, Notifications, and the command line's sentences. Hephaestus also has a large surface that programs read: commands, machine-readable output, labels, comment commands, configuration keys. Part of what the Owner reads is written by an Agent, from instruction templates that must be identical for every Agent (ADR-0012).

## Decision

**Translate what a person reads; never what a program reads.**

- Every sentence Hephaestus produces for a person is a message id with parameters, resolved in the core from resource files for English and Polish. Adapters and API handlers pass ids and parameters and compose no prose. A test fails when an id has no entry in a supported language.
- One function in the core resolves the language. Text on GitHub and Agent prose use the Task's configuration snapshot: the Project's `language` key if set, otherwise the installation's. The command line and Notifications use the installation's. The Dashboard uses the browser's choice, otherwise the installation's.
- Never translated: command names and options, `--json` output, labels and state names, comment-command verbs, `Depends-On:`, hidden markers, commit trailers, configuration keys, log lines, and the Glossary's terms.
- The Task's language travels in `task.json`. Instruction templates stay single-language and tell the Agent to write the prose fields of its result in the language named there.
- The Dashboard ships both languages in the image.

## Alternatives considered

- **Dashboard only.** Smallest, but the owner asked for everything the Owner reads.
- **One instruction template set per language.** Doubles the Methodology and breaks the rule that every Agent gets the same instructions for a step. Rejected: the language is data in the step's context instead.
- **Installation language only, no per-Project setting.** One installation working on a public English repository and a private Polish one would write the same language on both. Rejected.
- **Translating Glossary terms and state names.** They are also label and command vocabulary; two names for one thing would make the Dashboard disagree with GitHub. Rejected.

## Consequences

- Every story that writes a sentence adds two strings. The mechanism exists from the first command so nothing is retrofitted.
- Machine output is byte-identical in every language, which is testable and keeps scripts safe.
- An Agent may not follow the instruction to write in Polish perfectly; that is a quality matter for Review, not a contract Hephaestus can enforce.
- A third language is new resource files and no code.
- How the Dashboard holds its two languages is left to the first dashboard story.
