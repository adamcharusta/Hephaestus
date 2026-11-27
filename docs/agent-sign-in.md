# How each Agent signs in, and whether it is ready

Story 3.10, FR-25, FR-26, FR-18, AD-16, AD-20, AR-58, [ADR-0026](adr/0026-agent-sign-in-and-the-probe.md). You choose, for each Agent, how its CLI signs in: with an API key or with a subscription. You supply the
credential, and Hephaestus never signs in for you. A check then runs the Agent's CLI once, in a container of its own, and tells you whether the Agent is ready, so that you find out about a credential that does not work
before a Task does.

```bash
printf '%s' "$ANTHROPIC_API_KEY" | docker exec -i hephaestus hephaestus agent set-auth claude-code --mode api-key
claude setup-token                                                                                              # on your own machine, with the vendor's own tool; it prints a token
printf '%s' "$TOKEN" | docker exec -i hephaestus hephaestus agent set-auth claude-code --mode subscription
docker exec hephaestus hephaestus agent check claude-code     # runs the probe; exit code 0 when the Agent is ready, 1 when it is not
docker exec hephaestus hephaestus agent list                  # each Agent: mode, credential, status, when it was checked, and why it is unavailable
```

`set-auth` reads the credential from standard input, as `secret set` does, and refuses it as an argument. It stores the credential as a secret under the name the Agent's adapter gives for that mode
(`anthropic-api-key` or `claude-code-oauth-token`; `hephaestus secret list` shows it) and records the mode. An empty standard input keeps the secret already stored, so a mode can be chosen again without typing the
credential again; with none stored it is refused. A mode the Agent's adapter does not support is refused with the adapter's reason, and nothing is stored. Every command takes `--json`.

## The two modes

Both are supported for Claude Code and neither is the default: you choose.

| | API key | Subscription |
| --- | --- | --- |
| Who pays | The Anthropic account the key belongs to, by the token. | The plan of your Claude account, within its usage limits. |
| Credential | A key created in the Anthropic Console. | The token that `claude setup-token` prints. |
| Terms that apply | Anthropic's Commercial Terms of Service, which govern Claude API use. | Anthropic's Consumer Terms of Service for Free, Pro, Max, and Ultra plans, and what the vendor says about credentials. |
| Risk of automated use | Cost: an unattended run spends tokens until a limit stops it. | The vendor writes that the usage limits of these plans "assume ordinary, individual usage", and reserves enforcement "without prior notice". It also writes that developers "may not collect, store, or intermediate Claude.ai credentials or session tokens"; whether a self-hosted tool that keeps its own user's token in that user's installation does so, the text does not say. Your plan could be limited or ended for automated work. |

**The conservative choice is an API key with a spend cap at the vendor.** Set the cap on the Anthropic account, in the Console, before you let Hephaestus use the key: Hephaestus has no way to stop spending that the vendor allows, and
until it has its own spend limits (Story 9.4) the cap is the only one. A subscription raises the question above, which an API key does not. Read the terms yourself; this document is a reading by the project, not legal advice,
and the vendor's pages change without notice ([`docs/spikes/injection/results/06-terms/vendor-terms.md`](spikes/injection/results/06-terms/vendor-terms.md) quotes what they said on 2026-11-25).

Hephaestus implements no sign-in. The token comes from the vendor's own tool, on your own machine, and it is made for model requests only, as the vendor documents it. Claude Code's subscription credential is
this token and nothing else: the credential file of a signed-in CLI is not used.

**Codex CLI is supported with an API key only.** Its subscription credential is a file, `auth.json`, that holds a full ChatGPT account credential and must be kept and refreshed between runs, which Hephaestus does not allow a step
container to do; and the tools the account brings cannot be listed from the CLI, so they cannot be switched off. Codex CLI contacts `chatgpt.com` even with an API key, and `api.openai.com` and `ab.chatgpt.com`: an installation behind an allow-list of hosts needs all three
(`docs/spikes/injection/README.md`). The adapter of Codex CLI is not part of this release yet, so `agent set-auth codex-cli` and `agent check codex-cli` say that Hephaestus has no adapter for it and change nothing.

## Where the credential goes

- It travels on standard input and in the body of one request to the host, never in an argument or an address, and no answer of the host and no document contains it. The host's own log lines are masked, as they are for every secret.
- It is stored as a file on the `hephaestus-secrets` volume. The database holds the secret's name, the Agent's mode, and what the last check found, and nothing of the value.
- Only the container that runs that Agent's CLI receives it. When the runtime creates a step container, or a probe, it reads the secrets that Agent's invocation names, at that moment, from the secret store, and puts them in the
  container's environment. It reads no other secret. The value is in no label, no container name, no volume, and no journal entry; what the container prints is masked before it is kept. No GitHub credential ever enters the container.
- A secret that is not stored refuses the start, and nothing is created.

## The check

`agent check` asks the Agent's adapter for its probe: the CLI's own invocation with a one-word request, the same hardening as a step, and no model or Effort. The runtime runs it, waits for it to end, and the adapter reads what it
printed. It makes one request to the vendor: with an API key that costs a few tokens, and with a subscription it counts against the plan.

The probe runs in an image that this release fixes and that belongs to no Project:

| | |
| --- | --- |
| Image | `docker.io/library/debian:13-slim@sha256:8aa5a7bf5a1955bddbe7d74fcdd0b91ab30e74362a8b73af672b0481b35b44a0`, the Docker Official Image of Debian 13, named by the digest of its multi-platform index. A change of it is a change of the release. |
| Why it can be used | It meets the Project image contract: Linux, glibc, `bash`, and no declared volume. Nothing else is required of an image, and nothing else of this one is used. |
| Mounts | The Agent CLIs and the certificate bundle, read-only, and a temporary `HOME`. **No Task volume, no context, no Methodology.** |
| Network | The same as a step: an internal network of its own with an egress proxy of its own, allowing only the hosts the Agent's CLI declares (`api.anthropic.com` for Claude Code). |
| Limits | One processor, 2 GiB of memory, and two minutes, after which the container is removed. |

It is removed, with its proxy and network, when it ends; one that a crash of the host left carries the labels of the installation and the role `agent-probe`, by which recovery finds it. The image is fetched the first time a check needs it, so the first check needs access to Docker Hub and takes longer.
A check waits for the probe, which may take up to two minutes, and prints nothing until it ends; the command waits up to fifteen minutes. The host runs one probe at a time, so a second check waits for the first, and a probe that finishes after you changed how the Agent signs in records nothing.

| What the probe finds | Status | Reason shown |
| --- | --- | --- |
| The CLI answered | `ready` | none |
| The credential is not stored | `unavailable` | the name of the secret that is missing; no probe is run |
| The CLI reported an error (a credential it refused or one that has run out, or one of its own errors) | `unavailable` | the CLI reported an error, with its exit code |
| The CLI failed and did not say why | `unavailable` | its exit code |
| The container was stopped at its limit | `unavailable` | it did not end within two minutes |
| The container ended before it answered (out of memory, or lost) | `unavailable` | what the runtime saw |
| The probe could not run (Docker, the image, the proxy) | nothing recorded | the runtime's own message: the check says nothing of the Agent |

The finding is stored with the time of the probe on the capacity record of that credential, which is the Agent and the mode (AD-20). It holds a message id and its parameters, never a line the CLI printed. **An Agent
found unavailable has no capacity** (`AgentCapacity.HasCapacity` is false, which is what Dispatch will consult), and no step run is marked failed by it, because capacity is a fact about the Agent and not a failure of a Task. Storing a credential with `agent set-auth` forgets the finding of that mode's credential, because the credential may be another; keeping the one that is stored does not. A secret changed or removed with `secret set` or
`secret remove` does not forget it, so run `agent check` again after you change one: `agent list` shows whether the credential is stored. Dispatch, Task start, and the Dashboard do not read the record yet (Stories 3.14, 3.15, 3.22); today it is read by `agent list`, and
the check runs when you run `agent check`.
