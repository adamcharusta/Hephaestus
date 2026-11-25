# ADR-0016: Runtime and libraries

- **Status:** Accepted (System.CommandLine and Git versions were corrected by review)
- **Date:** 2026-11-25
- **Spine:** Stack

## Context

The brief preferred .NET 10. By the time of this decision .NET 11 had been released (10 November 2026). Versions below were checked against Microsoft's support policy page and the NuGet index on 2026-11-25; the Dashboard's were checked against the npm registry and nodejs.org on the same day.

## Decision

| Component | Version | Why |
| --- | --- | --- |
| .NET and ASP.NET Core | 10.0 LTS (10.0.15) | See below. |
| C# | 14 | |
| EF Core with SQLite | 10.0.15 | Matches the runtime. |
| Octokit | 14.0.0 | GitHub REST client. It does not sign the App's token; that is done with the platform's RSA API. |
| Docker.DotNet.Enhanced | 4.4.1 | Maintained by the Testcontainers project. The original Docker.DotNet has had no release since May 2023. |
| System.CommandLine | 2.0.15 | The line serviced alongside .NET 10. Version 3.0.0 belongs to the .NET 11 wave. |
| YamlDotNet | 18.1.0 | Project Configuration and methodology manifests. |
| xunit.v3, NSubstitute, Testcontainers | 4.0.3, 6.2.0, 4.17.0 | |
| Angular, with the Angular CLI | 22.3.0 | The Dashboard (ADR-0015). The owner chose Angular over React, which the brief had named. |
| TypeScript | 6.0.3 | Angular 22.3 accepts 6.0.x only; TypeScript 7 is not yet supported by it. |
| Node.js | 26.13.1 LTS | Build stage only. The runtime image contains the built Dashboard and no Node.js. |
| Vitest | 5.1.2 | The Angular CLI's unit test runner. |
| Git | 2.48 or later | Earlier versions skip object checking when fetching from a bundle (ADR-0010). |
| Docker Engine with Compose v2 | 29.0 or later | |

Logging uses the built-in `Microsoft.Extensions.Logging` with JSON output.

**.NET 10 over .NET 11.** Both reach end of support on 14 November 2028, because short-term releases are now supported for two years. Choosing 11 would not save a migration: either way the next move is to .NET 12. Version 10 has a year of patches behind it, and the coding agents that will write most of this code know it far better than a release two weeks old. The cost is doing without C# 15's union types, which would have suited failure classes and step results.

## Alternatives considered

- **.NET 11.** Rejected for the reasons above.
- **Serilog.** The built-in logger with JSON output meets the requirement for structured logs; one fewer dependency.
- **.NET Aspire.** There is one long-running service; nothing to orchestrate in development. Not adopted.
- **OpenTelemetry.** Deferred until there is a second deployment mode to observe.
- **React.** Named by the brief for a future dashboard. The owner chose Angular when the Dashboard moved into v0.1.
- **SignalR for live updates.** Server-Sent Events do the one-way job with no package and no exception to the read-only rule (ADR-0015).
- **A JSON Schema validation library.** Results are validated by strict deserialization into the core's own types; schemas given to the CLIs are generated from those types.

## Consequences

- An upgrade to .NET 12 LTS is due before November 2028.
- The build needs Node.js as well as the .NET SDK; contributors who do not touch the Dashboard still build it in the image.
- Agent CLI versions are not listed here; `deploy/agents.lock` pins them per release (ADR-0011).
