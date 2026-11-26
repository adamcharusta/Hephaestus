# Hephaestus

[![SonarQube Cloud](https://sonarcloud.io/images/project_badges/sonarcloud-dark.svg)](https://sonarcloud.io/summary/new_code?id=adamcharusta_Hephaestus)

A self-hosted orchestrator that turns labelled GitHub issues into validated, independently reviewed pull requests by
driving Claude Code and Codex CLI in hardened containers. It is MIT-licensed and free to run; the Agents and their costs
are your own.

**Status:** under construction. There is no release yet; the image is built from this repository. The repository holds the
plan (`_bmad-output/`), the architecture and its decision records (`docs/`), and the code the stories build on. Release
channels arrive with Epic 1.

## Run

You need Docker Engine 29 or later with Compose v2, and nothing else: no other server, paid service, or cloud service.

```bash
docker compose -f deploy/compose.yaml up -d      # builds the image, starts the container `hephaestus`, waits until it is healthy
docker exec hephaestus hephaestus version        # the command line: the same executable, run inside the container
docker exec hephaestus hephaestus --help         # the commands and options
docker compose -f deploy/compose.yaml down       # stop it
```

The command line calls the host's control API over a Unix domain socket inside the container. The container publishes no
port and listens on no network address. Every command takes `--json` to print one JSON document, prints errors on standard
error, and exits with 0, 1 (a failure), or 2 (not understood). Set `HEPHAESTUS_LANGUAGE=pl` (in the environment of
`docker compose up`, or with `docker exec -e`) for Polish sentences; command names, options, and `--json` output never change.
Logs are one JSON object per line: `docker logs hephaestus`. More in [`deploy/README.md`](deploy/README.md).

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
