# deploy

Deployment files of the spine's source tree. Each is added by the story that first needs it:

| File | Story | Holds |
| --- | --- | --- |
| `compose.yaml` | 1.4, 1.5, 3.1 | The `hephaestus` service: one container, no published port, healthy when its API answers; the `hephaestus-data`, `hephaestus-secrets`, and `hephaestus-agents` volumes |
| `Dockerfile` | 1.4, 1.5, 3.1 | The `hephaestus` image for linux/amd64 and linux/arm64, with the mount points of the three volumes and `agents.lock`, and no Agent binary |
| `agents.lock` | 3.1 | The Agent CLIs this release was tested with: one version of each, and the SHA-256 of every executable, per architecture and C library ([`docs/agent-clis.md`](../docs/agent-clis.md)) |
| `tools.Dockerfile` | the story that first starts a helper container | |

## Run Hephaestus

```bash
docker compose -f deploy/compose.yaml up -d
docker exec hephaestus hephaestus version
```

- **One executable.** `hephaestus serve` is the container's process, the host with its control API; every other
  argument is the command line, a client of that API. `serve` is not a command of the command tree.
- **The control socket** is `/run/hephaestus/control.sock` inside the container (`HEPHAESTUS_CONTROL_SOCKET`), owned by the
  user the host runs as and readable by that user alone. The API is on that socket and on no network address.
- **Volumes.** `hephaestus-data` at `/var/lib/hephaestus/data` (`HEPHAESTUS_DATA_DIR`) holds the SQLite database
  `hephaestus.db`, created and migrated at start in write-ahead-log mode. `hephaestus-secrets` at
  `/var/lib/hephaestus/secrets` (`HEPHAESTUS_SECRETS_DIR`) holds one file per secret, mode 0600, in a directory only the
  host's user can enter. Only the `hephaestus` service mounts either; a copy of both volumes is a backup of the installation.
  `docker compose down --volumes` deletes them, and with them the installation.
- **Agent CLIs.** `hephaestus-agents` at `/var/lib/hephaestus/agents` (`HEPHAESTUS_AGENTS_DIR`) holds the pinned Claude Code and Codex CLI executables.
  At every start, after the database is migrated and before the loop's first tick, the host downloads what the volume lacks from each vendor's
  official channel, verifies every executable against `agents.lock`, and stops, naming the file, unless all of them match. The first start therefore needs the
  vendors' download hosts and about a gigabyte of download; later starts download nothing. The volume is not installation state, so it is not part of a
  backup. [`docs/agent-clis.md`](../docs/agent-clis.md) has the details.
- **Secrets.** `docker exec -i hephaestus hephaestus secret set <name>` reads the value from standard input; see the
  [README](../README.md#secrets).
- **Health.** The container's health check is `hephaestus version --json`, so it is healthy when the API answers.
- **Language.** The installation's language is a setting: `docker exec hephaestus hephaestus setting set language pl` (`en` or `pl`), used by the
  next command. `HEPHAESTUS_LANGUAGE` (`en` by default) is only the language of an installation that has set none, and of a command that cannot
  reach the host, so an installation that already sets it keeps its language until the setting is made.
- **Image.** Compose builds it from this repository when it is not on the machine; the release story (1.13) publishes it.
  Build it by hand from the repository root with `docker build -f deploy/Dockerfile -t hephaestus .`.
