# Vendor terms on downloading and redistributing the Agent CLIs

Read on 2026-11-25. Quotations are verbatim. This is a reading by the project, not legal advice. The pages on `code.claude.com` are live documentation with no version or date: what they said on that day is quoted here, and they should be read again before a release relies on them.

## Claude Code (Anthropic)

| Source | What it says |
| --- | --- |
| [LICENSE.md in anthropics/claude-code](https://github.com/anthropics/claude-code/blob/6f27711e0498f3a631916231e1d8149db6ebc884/LICENSE.md), at the commit that last changed it | "© Anthropic PBC. All rights reserved. Use is subject to Anthropic's Commercial Terms of Service." No licence to redistribute is granted. |
| [Legal and compliance, "License"](https://code.claude.com/docs/en/legal-and-compliance) | Use is subject to the Commercial Terms of Service "for Team, Enterprise, and Claude API users" and the Consumer Terms of Service "for Free, Pro, Max, and Ultra users". |
| [Legal and compliance, "Can customers offer Claude Code in their products?"](https://code.claude.com/docs/en/legal-and-compliance) | "preinstalling or running Claude Code in your products or services (e.g. in hosted sandboxes or other agent infrastructure) requires agreeing to our Commercial Terms of Service" and two conditions: "The Claude Code binary must not be modified. Claude Code must be installed and run as published by Anthropic, and customers may not remove, disable, or restrict any authentication method built into it"; and "Customers may not pay for, resell, or intermediate Claude usage on their end users' behalf. Each end user must authenticate with their own Anthropic API key, Claude subscription plan credentials, or 3P inference provider credential". |
| [Legal and compliance, "Authentication and credential use"](https://code.claude.com/docs/en/legal-and-compliance) | "developers may not collect, store, or intermediate Claude.ai credentials or session tokens — sign-in to a Claude account must complete through Anthropic's own flow." It does not "prevent an end user from signing in to the unmodified Claude Code binary with their own Claude subscription, including where a platform hosts Claude Code". "Advertised usage limits for Pro, Max, and Ultra plans assume ordinary, individual usage". Enforcement "without prior notice". |
| [Advanced setup, "Install a specific version"](https://code.claude.com/docs/en/setup) | The official installer accepts a version number: `curl -fsSL https://claude.ai/install.sh \| bash -s 2.1.89`. |
| [Advanced setup, "Binary integrity and code signing"](https://code.claude.com/docs/en/setup) | "Each release publishes a `manifest.json` containing SHA256 checksums for every platform binary. The manifest is signed with an Anthropic GPG key". It names direct download as a supported path: "If you download directly from the `claude-code-releases` bucket or use the native installer, verify integrity with the manifest signature above." Repository `https://downloads.claude.ai/claude-code-releases`, key fingerprint `31DD DE24 DDFA B679 F42D 7BD2 BAA9 29FF 1A7E CACE`. Signatures exist from release 2.1.89. |
| [Advanced setup, "Disable auto-updates"](https://code.claude.com/docs/en/setup) | "To block all update paths, including manual updates, set `DISABLE_UPDATES` instead. Use this when you distribute Claude Code through your own channels and need users to stay on the version you provide." |
| [Advanced setup, "Install on network storage"](https://code.claude.com/docs/en/setup) | Placing the binary "with a Linux package manager or your own deployment tooling" is described as a normal layout. |

**Reading.**

- Downloading the unmodified binary of a pinned version straight from `downloads.claude.ai/claude-code-releases` and verifying it against the signed manifest is a path the vendor documents. Fetching at startup, on the Owner's machine, for the Owner's own use, is that path. **Permitted.**
- Shipping the binary inside a public Hephaestus image is redistribution, for which no licence is granted. **Not permitted without an agreement.** This confirms AD-8's rule that Hephaestus images contain no Agent binary, and it constrains the fallback: a "Hephaestus base image" that contains Claude Code could not be published by the project; each Owner would have to build it locally.
- The binary must stay unmodified and no authentication method may be removed, disabled, or restricted. Hephaestus passes flags and environment variables and edits nothing. Choosing one authentication mode per installation is the Owner's configuration of their own installation, not a product restricting its users; this reading should be kept under review.
- Hephaestus is self-hosted: the Owner is the end user and supplies their own credential. Hephaestus never performs a sign-in and stores the credential only in the Owner's own installation. Whether that storage is "intermediating" a subscription credential is not settled by the text; an API key carries no such question. This was already recorded as a policy risk (FR-25) and the spike does not change it.

## Codex CLI (OpenAI)

| Source | What it says |
| --- | --- |
| [LICENSE in openai/codex at rust-v0.172.0](https://github.com/openai/codex/blob/rust-v0.172.0/LICENSE) | Apache License, Version 2.0, "Copyright 2025 OpenAI". Section 4 permits reproducing and distributing copies "in any medium, with or without modifications, and in Source or Object form" on condition that recipients receive a copy of the licence and that notices are kept. |
| [Releases of openai/codex](https://github.com/openai/codex/releases/tag/rust-v0.172.0) | The official channel for prebuilt binaries. Each asset carries a SHA-256 digest and a `.sigstore` bundle. |

**Reading.** Fetching at startup is **permitted**, and so is redistribution under the Apache-2.0 conditions. Use of the OpenAI service with the binary remains under OpenAI's service terms, which this spike did not review beyond the licence.
