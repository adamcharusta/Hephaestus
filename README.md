# Hephaestus

[![SonarQube Cloud](https://sonarcloud.io/images/project_badges/sonarcloud-dark.svg)](https://sonarcloud.io/summary/new_code?id=adamcharusta_Hephaestus)

A self-hosted orchestrator that turns labelled GitHub issues into validated, independently reviewed pull requests by
driving Claude Code and Codex CLI in hardened containers. It is MIT-licensed and free to run; the Agents and their costs
are your own.

**Status:** under construction. There is no release yet and nothing to install or run. The repository holds the plan
(`_bmad-output/`), the architecture and its decision records (`docs/`), and the solution skeleton that the stories build
on. Release channels and installation arrive with Epic 1.

## Build and test

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and Python 3.

```bash
dotnet build Hephaestus.slnx    # warnings are errors; nullable reference types are on
dotnet test --solution Hephaestus.slnx
python3 eng/check-licenses.py   # after a build: every dependency carries an allowed license
python3 -m unittest discover -s eng -p "test_*.py"   # the gate scripts' own tests
```

The commands that measure coverage the way CI does are in [`AGENTS.md`](AGENTS.md). Pull requests into `develop` or
`main` are built and tested by GitHub Actions, which also fails them below 80% line coverage or on a dependency whose
license is not on the allowed list. How a change reaches each branch, and which checks guard it, is in
[`docs/branching.md`](docs/branching.md).

## Where to read more

- [`docs/architecture.md`](docs/architecture.md) and [`docs/adr/`](docs/adr/): how Hephaestus is built, and why.
- [`docs/security/`](docs/security/): the threat model.
- [`LICENSE`](LICENSE): MIT.
