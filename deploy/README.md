# deploy

Deployment files of the spine's source tree. Each is added by the story that first needs it:

| File | Story | Holds |
| --- | --- | --- |
| `compose.yaml` | 1.4, 1.5 | The `hephaestus` service: one container, no published port, healthy when its API answers; the `hephaestus-data` and `hephaestus-secrets` volumes |
| `Dockerfile` | 1.4, 1.5 | The `hephaestus` image for linux/amd64 and linux/arm64, with the mount points of the two volumes |
| `agents.lock` | 3.1 | |
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
- **Secrets.** `docker exec -i hephaestus hephaestus secret set <name>` reads the value from standard input; see the
  [README](../README.md#secrets).
- **Health.** The container's health check is `hephaestus version --json`, so it is healthy when the API answers.
- **Language.** `HEPHAESTUS_LANGUAGE` is `en` (default) or `pl`. Story 1.9 makes it a setting of the installation.
- **Image.** Compose builds it from this repository when it is not on the machine; the release story (1.13) publishes it.
  Build it by hand from the repository root with `docker build -f deploy/Dockerfile -t hephaestus .`.
