# Hephaestus in C4 Views

Three views of the same system at increasing zoom, following the [C4 model](https://c4model.com): who uses it and what it talks to, what runs where, and what is inside the one process that matters. The diagrams describe the design for releases v0.1 to v0.3; nothing here is implemented yet. The reasoning behind the shapes is in [architecture.md](architecture.md) and the [decision records](adr/).

## Level 1: System context

Hephaestus sits between the Owner's GitHub repositories and the coding agents the Owner already pays for. The Owner never talks to an agent directly; they write issues and review pull requests.

```mermaid
flowchart TB
    Owner([Owner<br/>writes issues, labels them,<br/>reviews and approves pull requests])
    Hephaestus[Hephaestus<br/>turns labelled issues into<br/>validated, reviewed pull requests]
    GitHub[(GitHub<br/>issues, labels, branches,<br/>pull requests, reviews)]
    Anthropic[(Anthropic<br/>models behind Claude Code)]
    OpenAI[(OpenAI<br/>models behind Codex CLI)]
    Notify[(Notification services<br/>email, phone push; from v0.2)]

    Owner -->|issues, labels, comments, approvals| GitHub
    Owner -->|configures, starts, asks status<br/>through the command line;<br/>follows work in the Dashboard| Hephaestus
    Hephaestus -->|polls for work,<br/>pushes branches, opens pull requests,<br/>comments, merges after approval| GitHub
    Hephaestus -->|runs Claude Code| Anthropic
    Hephaestus -->|runs Codex CLI| OpenAI
    Hephaestus -->|alerts| Notify
    Notify -->|needs your attention| Owner
```

## Level 2: Containers

Everything runs on one Docker host. Apart from `hephaestus`, every container lives for a single step of a single Task; the egress proxy is one of them, one per step run ([ADR-0021](adr/0021-egress-proxy.md)).

```mermaid
flowchart TB
    Owner([Owner])
    GitHub[(GitHub)]
    Vendors[(Anthropic and OpenAI)]

    subgraph Host[Docker host]
        subgraph Orchestrator[hephaestus container · .NET 10 · long-running]
            Process[Hephaestus process<br/>orchestrator loop, control API,<br/>dashboard listener, GitHub gateway,<br/>workspace runtime]
        end
        Socket[/Docker socket/]

        subgraph NetworkA[Step run network · internal, no route out · one per step run]
            StepAgent[Agent step container<br/>Project image + injected Agent CLI<br/>one Agent credential]
            ProxyA[Egress proxy · hephaestus egress-proxy<br/>unprivileged, no credentials<br/>Project list + this Agent's hosts]
        end
        subgraph NetworkB[Step run network · internal, no route out]
            StepPlain[Validation, checkout, or checkpoint container<br/>no credentials]
            ProxyB[Egress proxy<br/>Project list alone]
        end

        Data[(data volume<br/>SQLite database,<br/>raw agent output)]
        Secrets[(secrets volume<br/>credential files)]
        Mirrors[(mirrors volume<br/>one bare mirror per Project)]
        Binaries[(agent binaries volume<br/>pinned Claude Code and Codex,<br/>fetched and verified at startup)]
        TaskVolume[(Task volume<br/>one per Task: a fresh checkout<br/>before every step)]
    end

    Owner -->|docker exec: CLI in client mode| Process
    Owner -->|"browser on 127.0.0.1:8717<br/>Dashboard Token, reads only"| Process
    Process --- Data
    Process --- Secrets
    Process --- Mirrors
    Process -->|creates, limits, stops containers;<br/>copies context files in, one bundle file out| Socket
    Socket --> StepAgent
    Socket --> StepPlain
    StepAgent --- TaskVolume
    StepPlain --- TaskVolume
    Binaries -.->|read-only| StepAgent
    Process <-->|HTTPS: poll, push, API| GitHub
    StepAgent -->|all egress| ProxyA
    StepPlain -->|all egress| ProxyB
    ProxyA -->|HTTPS: model requests| Vendors
    ProxyA -->|HTTPS: package registries<br/>and other public hosts| Internet[(Public internet)]
    ProxyB -->|public hosts, allowlist when set| Internet
```

| Container | Lifetime | Holds credentials | Notes |
| --- | --- | --- | --- |
| `hephaestus` | Long-running | GitHub App key, all Agent credentials (from the secrets volume) | The only container with the Docker socket. Root-equivalent on the host. Serves the Dashboard on a port published on the host's loopback address only; it is on no network a step container or the egress proxy is on. |
| Agent step container | One step | The credential of the one Agent running that step | Runs as a non-root user with all capabilities dropped. The agent's own sandbox is off; the container is the boundary. |
| Validation container | One command | None | Runs one of the Project's build or test commands on a fresh checkout, in the Project image. |
| Checkout and checkpoint containers | Seconds | None | Run a small tools image with git only: re-create the checkout before a step, and afterwards write one commit and one bundle file. |
| Egress proxy | One per step run, for the step's duration | None | `hephaestus egress-proxy`, a command of the `hephaestus` image, on the step run's internal network and on `hephaestus-outbound`. The only exit from the step run's network. Refuses loopback, link-local, and private destinations and applies each step's allowed hosts: the Project's list, plus an Agent's own hosts in that Agent's container only. Unprivileged, with no volume and no Docker socket. |

No volume is shared between Tasks, and a Task volume is never mounted into the `hephaestus` container. Work crosses that line only as a git bundle.

## Level 3: Components of the Hephaestus process

The process is a modular monolith. The core defines ports; adapters implement them. An adapter never calls another adapter.

```mermaid
flowchart TB
    subgraph HostProj[Hephaestus.Host]
        Api[Control API<br/>Unix domain socket<br/>everything that changes state]
        Dash[Dashboard listener<br/>TCP on loopback, token required<br/>static files, reads, change stream]
        Cli[CLI commands<br/>client mode]
        Startup[Startup<br/>backup, migrate, reconcile]
    end

    subgraph Core[Hephaestus.Core]
        Loop[Orchestrator loop<br/>the only writer of Task state]
        Machine[Task state machine<br/>and step sequencing]
        Policies[Policies<br/>trust admission, queue order,<br/>reviewer choice, bounds]
        Context[Context assembly<br/>files for each step run]
        Masker[Masker<br/>removes secret values from<br/>everything written or sent]
        Methodology[Methodology model<br/>step templates as data]
        Ports{{Ports}}
    end

    subgraph Adapters[Adapter projects]
        Agents[Hephaestus.Agents<br/>Claude Code adapter, Codex adapter:<br/>invocation, stream parsing,<br/>failure classification]
        Gateway[Hephaestus.GitHub<br/>poller, API writes,<br/>mirrors, bundle import, push]
        Runtime[Hephaestus.Workspaces<br/>volumes, step containers,<br/>bundle exchange, checkpoints]
        Store[Hephaestus.Persistence<br/>Tasks, step runs, signal inbox,<br/>effect journal, configuration]
    end

    Cli --> Api
    Browser(["Dashboard in the Owner's browser<br/>Angular, built from Hephaestus.Dashboard"]) --> Dash
    Dash -->|reads only| Ports
    Loop -.->|announces what changed| Dash
    Api -->|appends Signals| Ports
    Startup --> Loop
    Loop --> Machine
    Loop --> Policies
    Loop --> Context
    Loop --> Methodology
    Loop --> Ports
    Ports -.implemented by.-> Agents
    Ports -.implemented by.-> Gateway
    Ports -.implemented by.-> Runtime
    Ports -.implemented by.-> Store
    Gateway -->|poller appends Signals| Ports
    Runtime -->|appends step-exited Signals| Ports
```

| Component | Responsibility | Never does |
| --- | --- | --- |
| Orchestrator loop | Applies Signals in order, one transaction each; advances Tasks; records intents and results around every outside effect. | Hold state only in memory, or wait for a running step. |
| Policies | Decide who is trusted, what runs next, who reviews, when to stop. | Talk to GitHub or Docker. |
| Agent adapters | Describe how to invoke a CLI for a step and interpret what it prints. | Start a process or call Docker. |
| GitHub gateway | The single path to GitHub: reads events, writes effects, owns mirrors, pushes. | Decide trust or change Task state. |
| Workspace runtime | Runs what it is given in a container with limits; moves bundles and context files across the boundary. | Know which Agent it is running. |
| Persistence | Stores what the core asks it to. | Contain rules. |
| Dashboard listener | Serves the Dashboard and answers reads through a read-only store; forwards the loop's announcements as Server-Sent Events. | Append a Signal, change configuration, touch a secret, or accept anything from the browser but a read. |

## A Task from label to pull request

A dynamic view of the default path, for readers who prefer to follow one request through. Before each step a fresh checkout of the last pushed commit is made; that is left out of the diagram.

```mermaid
sequenceDiagram
    actor Owner
    participant GH as GitHub
    participant H as Hephaestus
    participant A as Agent step container
    participant V as Validation container

    Owner->>GH: apply Marker Label to an issue
    H->>GH: poll, read timeline (who applied the label?)
    H->>H: admit Signal, Task is Queued
    H->>GH: comment "started", set state label
    H->>A: Readiness (primary Agent)
    A-->>H: clear enough
    H->>A: Implementation (primary Agent)
    A-->>H: summary
    H->>GH: push checkpoint to Task branch
    H->>V: run Validation commands
    V-->>H: exit status
    H->>A: Review (the other Agent)
    A-->>H: Findings
    alt blocking Findings or failed Validation, rounds left
        H->>A: Correction (implementing Agent)
        H->>V: Validation again
    end
    H->>GH: open pull request, link issue, comment
    Owner->>GH: review, approve
```
