# Hephaestus

[![SonarQube Cloud](https://sonarcloud.io/images/project_badges/sonarcloud-dark.svg)](https://sonarcloud.io/summary/new_code?id=adamcharusta_Hephaestus)

A self-hosted orchestrator that turns labelled GitHub issues into validated, independently reviewed pull requests by
driving Claude Code and Codex CLI in hardened containers. It is MIT-licensed and free to run; the Agents and their costs
are your own.

**Status:** under construction. There is no release yet; the image is built from this repository. The repository holds the
plan (`_bmad-output/`), the architecture and its decision records (`docs/`), and the code the stories build on. Release
channels arrive with Epic 1.

## Run

You need Docker Engine 29 or later with Compose v2, and nothing else: no other server, paid service, or cloud service. The `hephaestus` container holds the Docker socket, which is how it creates the
containers that Tasks run in; it runs as a user that is not root, so name the group that owns the socket first (`export HEPHAESTUS_DOCKER_GID=$(stat -c %g /var/run/docker.sock)`; the default, 999, is the usual one). The first start
downloads the Agent CLIs this release was tested with (about a gigabyte) from Anthropic's and OpenAI's own download hosts, so the machine needs
to reach `downloads.claude.ai` and `github.com` then; see [Agent CLIs](#agent-clis).

```bash
docker compose -f deploy/compose.yaml up -d      # builds the image, starts the container `hephaestus`, waits until it is healthy
docker exec hephaestus hephaestus version        # the command line: the same executable, run inside the container
docker exec hephaestus hephaestus --help         # the commands and options
docker compose -f deploy/compose.yaml down       # stop it
```

The command line calls the host's control API over a Unix domain socket inside the container. The container publishes no
port and listens on no network address. Every command takes `--json` to print one JSON document, prints errors on standard
error, and exits with 0, 1 (a failure), or 2 (not understood). Set the installation's language with
`docker exec hephaestus hephaestus setting set language pl` (`en` or `pl`; a new installation is in English): the next command
writes its sentences in Polish. `HEPHAESTUS_LANGUAGE` only names the language of an installation that has set none, and of a command
that cannot reach the host. Command names, options, and `--json` output never change.
Logs are one JSON object per line: `docker logs hephaestus`. More in [`deploy/README.md`](deploy/README.md).

## Agent CLIs

Hephaestus drives Claude Code and Codex CLI, and brings exactly the versions it was tested with. Neither is in the Hephaestus image, and neither is in your
Projects' images: `deploy/agents.lock` pins one version of each and the SHA-256 of every executable, and at every start the host downloads what the
`hephaestus-agents` volume lacks from the vendor's official channel, checks each file against the lock, and stops, naming the file, if one does not match.
Nothing unverified is kept in the volume, and a volume that already holds the verified files downloads nothing. A CA certificate bundle copied from the image's own
certificate store is placed beside them at every start. The first start needs access to `downloads.claude.ai` and `github.com`; the volume is a cache, not installation
state, and `docker compose down --volumes` deletes it with the rest. [`docs/agent-clis.md`](docs/agent-clis.md) says how the lock is written and how a pin is changed.

## Step containers

Every step of a Task will run in a container that cannot touch your files, your other Projects, or Docker itself: it runs as user `10001:10001` with every capability dropped, `no-new-privileges`,
Docker's default seccomp profile, a process limit, the Task's processor and memory limits (never above `hephaestus limit show`), no published port, and no route to the host. Its only mounts are the Task's volume,
the context and Methodology read-only, the Agent CLIs read-only, and a temporary `HOME`. Only the `hephaestus` container holds the Docker socket, so the orchestrator is equivalent to root on the host,
and no Task volume is ever mounted into it. The runtime that creates these containers is in place and tested against a real Docker Engine; nothing starts a step yet. [`docs/step-containers.md`](docs/step-containers.md)
has the definition, how the C library of a Project's image is established, and the checks.

## Secrets

A credential is handed to Hephaestus on standard input; a value given as an argument is refused. It is stored as a file on the `hephaestus-secrets` volume, which only the `hephaestus` container mounts;
the database on the `hephaestus-data` volume holds the secret's name and nothing of its value.

```bash
printf '%s' "$ANTHROPIC_API_KEY" | docker exec -i hephaestus hephaestus secret set anthropic-api-key
docker exec hephaestus hephaestus secret list                  # names only
docker exec hephaestus hephaestus secret remove anthropic-api-key
```

Pass `-i` to `docker exec` so the value reaches the command. A name has 1 to 64 characters (lowercase letters, digits, `-`,
`_`); a value has 8 to 65,536 characters, and one trailing line break is dropped. A value given as an argument, an empty
value, and one that is too short are refused and nothing is stored. Secrets survive a restart of the container. The two
volumes are the whole installation state, so back up both, with the container stopped so the copy of the database is consistent; the secrets volume is plain files and the host must protect it.

Hephaestus writes its own log lines through a masker: a stored value that a line holds is replaced by `***`, and so are its
base64 and URL-encoded forms. A secret you add, change, or remove is masked, or no longer masked, from the next line, without a
restart; a file placed on the volume by hand is noticed at the next change or restart. A value shorter than 8 characters is never
masked. A value that spans lines, such as a PEM private key, is masked whole and line by line. Only the host's own log lines pass
through the masker so far, and other encodings of a value are not caught.

## GitHub identity

Hephaestus acts on GitHub as its own GitHub App, so that you can approve the pull requests it opens. Create the App with exactly the
permissions it needs, install it on your repositories, and connect it, as [`docs/github-app.md`](docs/github-app.md) describes:

```bash
docker exec -i hephaestus hephaestus github connect --app-id 123456 < hephaestus.pem   # the App's private key on standard input
docker exec hephaestus hephaestus github check                                          # identity, repositories, permissions
```

Both print the App, the repositories it is installed on, and each required permission as present or missing (on the App and on
every installation of it), and warn when `workflows` or administration is granted. The exit code says whether GitHub answered;
`--json` has a `ready` field for whether the permissions are enough. The private key is stored as the secret `github-app-private-key`; it is stored only
once GitHub has accepted it, and a failure says whether GitHub was unreachable or refused the credential. No feature uses the
identity yet.

## Trusted Users

The Trusted Users are the GitHub users whose labels, answers, and commands Hephaestus will act on. Connect the GitHub App first (the
commands look a login up on GitHub), then:

```bash
docker exec hephaestus hephaestus trusted-user add adamcharusta   # GitHub's numeric id is stored; the login is for display
docker exec hephaestus hephaestus trusted-user list                # login, GitHub user id, when added (UTC)
docker exec hephaestus hephaestus trusted-user remove adamcharusta
```

A user is identified by GitHub's numeric id, so one who changes their login stays trusted and one who takes over an old login does
not inherit anything. Adding a login GitHub does not know fails, and so does adding the App's own bot account (`<slug>[bot]`).
`remove` works from what `list` shows, without asking GitHub, so trust can be taken back while GitHub is down; a user who has since changed their login
can be removed by the new one. The users live in the database on the `hephaestus-data` volume and survive a restart. The Marker Label
is the first thing that reads the list (see [Opting an issue in](#opting-an-issue-in)); a reply on the issue of a waiting Task is acted on (see [When Hephaestus needs a decision](#when-hephaestus-needs-a-decision)), commands are not yet.

## Aliases, limits, protected paths, and settings

An Alias is a name you choose for an Agent with a Model, or for an Effort. Labels will carry it (`primary:opus5.5`), so a new Model needs
only a new Alias, not a new Hephaestus. The limits are the ceilings no repository's `hephaestus.yml` can raise, and the protected
paths are the files a Task's change must not touch once the GitHub gateway enforces them (a later story).

```bash
docker exec hephaestus hephaestus alias add opus5.5 --agent claude-code --model claude-opus-5-5   # Agents: claude-code, codex-cli
docker exec hephaestus hephaestus alias add deep --effort high                                    # Efforts: low, medium, high, xhigh, max
docker exec hephaestus hephaestus alias list
docker exec hephaestus hephaestus alias remove deep
docker exec hephaestus hephaestus limit set --task-duration 2h --cpus 2 --memory 4g               # only the options you give change
docker exec hephaestus hephaestus limit show
docker exec hephaestus hephaestus protected-path list                                             # a new installation: .github/** and hephaestus.yml
docker exec hephaestus hephaestus protected-path add 'docs/**'
docker exec hephaestus hephaestus protected-path remove 'docs/**'
docker exec hephaestus hephaestus setting list
docker exec hephaestus hephaestus setting set language pl
docker exec hephaestus hephaestus setting set pollingInterval 120                                 # how often GitHub is polled, in seconds
docker exec hephaestus hephaestus setting set effectRetries 8                                     # how often a change on GitHub is tried again, 0 to 20
docker exec hephaestus hephaestus config show                                                     # all of it; secrets by name only
```

An Alias name is lower case letters, digits, `.`, `_`, and `-` (at most 40 characters); a Model is written as its vendor spells it.
An Alias that exists is not redefined: remove it first. A duration is `90m`, `2h30m`, or `1d` (1 minute to 7 days), processors are a
number from `0.1` to `1024`, and memory is `512m` or `4g` (64m to 4096g). A limit nobody set is shown as `none`. The limits and the
protected paths are stored and shown; nothing checks a Project Configuration against the limits, or a Task's changes against the
protected paths, yet. Everything is in the database on the `hephaestus-data` volume and survives a restart.

## Projects

A Project is a GitHub repository Hephaestus works on, and on no other. Connect the GitHub App and install it on the repository first
([`docs/github-app.md`](docs/github-app.md)); then register the repository with its Base Branch, the branch that Tasks start from and
that approved pull requests are merged into:

```bash
docker exec hephaestus hephaestus project add acme/shop --base-branch main
docker exec hephaestus hephaestus project list                  # name, GitHub repository id, Base Branch, when registered; --all adds removed Projects
docker exec hephaestus hephaestus project remove acme/shop
docker exec hephaestus hephaestus project check acme/shop       # reads hephaestus.yml from the Base Branch and says what is wrong with it
```

`project add` looks the repository up as the App, so it fails, and says which is wrong, when the App is not installed on the
repository (or the repository does not exist) or when the Base Branch does not exist. It then says whether GitHub reports the Base
Branch as protected (the `protected` flag GitHub gives the branch), and recommends protecting it against direct pushes when it does not; Hephaestus has no administration permission, so
it cannot read what a protection requires and does not claim anything about it. A Project is stored under GitHub's numeric repository
id, so a repository that is renamed is still the same Project: register it again under its new name to refresh the name shown, or
remove it by its new name. Registering a Project again with another Base Branch changes the Base Branch. `project remove` retires the
Project and keeps its record and everything recorded under it; registering it again brings it back. The Projects live in the database
on the `hephaestus-data` volume and survive a restart.

### Noticing what happens on GitHub

While the host runs it polls GitHub for each registered Project (and for no other repository): an issue labelled, unlabelled, closed, or
reopened, and a comment created or edited, each become one Signal in a persisted inbox, with the numeric id of the user GitHub names as the
actor. There is no public endpoint, so it works behind a home router. Requests are conditional, GitHub's `304` and any minimum interval
and rate-limit wait are respected, and a restart loses or doubles nothing.

```bash
docker exec hephaestus hephaestus setting set pollingInterval 120   # seconds, 10 to 3600; the default is 60
```

[`docs/polling.md`](docs/polling.md) describes what is watched, what a Signal holds, and the waits.

### Opting an issue in

An issue becomes a Task when a Trusted User applies the label `hephaestus` to it on GitHub; the label from anyone else does nothing:
Hephaestus records who it was and why it was refused, and says on the issue, once for each user, that the label was ignored because that user
is not a Trusted User. A comment or a `/hephaestus` command from someone who is not a Trusted User is recorded and gets no reply. The Task starts `queued` with the issue's title and body, as GitHub showed them with the label event, snapshotted and its
branch `hephaestus/<issue-number>-<slug>` chosen. Its state shows on the issue as one label, `hephaestus:queued`, which GitHub's issue filter
lists; Hephaestus puts it back if you change it by hand. Nothing works on the Task yet: dispatch, withdrawing, and the Dashboard are later
stories. Ask whether the orchestrator loop is running:

```bash
docker exec hephaestus hephaestus health          # Loop: alive, last tick 2026-11-27T12:00:03Z.  Exit code 1 when it is not alive
```

[`docs/intake.md`](docs/intake.md) says what the loop does with each Signal, what Hephaestus says on an issue when it ignores something, what the snapshot holds, and how the branch is named.
[`docs/state-labels.md`](docs/state-labels.md) lists the state labels and says what happens when GitHub cannot be reached or refuses a change:
the Task moves to `waiting-for-owner`, and in v0.1 a Task whose issue can no longer be written to is visible only in the log.

### When Hephaestus needs a decision

When it cannot go on with a Task, Hephaestus posts one specific question on the Task's issue, **Hephaestus needs a decision.**, with the options
lettered one per line, and the Task waits in `waiting-for-owner` while other Tasks go on. The same happens when a change to the issue could not
be made within the retries, and the comment then says which one. **Reply on the issue to answer**: the first reply from a Trusted User puts
the Task back in the Queue with your reply recorded as the answer among its admitted comments, and the issue shows `hephaestus:queued` again. A reply from anyone else changes nothing and gets no
reply, and a comment that begins with `/hephaestus` is a command, not an answer. Or remove the label `hephaestus` from the issue to cancel the
Task (closing the issue closes it). The notification channels are a later release: in v0.1 the comment
is how you are told. [`docs/escalation.md`](docs/escalation.md) has the shape of the comment and what is done to text an Agent wrote.

### The Project's configuration file

A Project is configured by `hephaestus.yml` at the root of its Base Branch: the image its Tasks run in, the Validation commands,
and the defaults for Agents, Methodology, and limits. [`docs/project-configuration.md`](docs/project-configuration.md) describes
every key and gives complete examples for .NET, Node.js, and Python, each with a stock image. `project check` reads the file from
the Base Branch (and from no other branch) and says whether it is valid and which commit it was read from, that the file is missing,
or every invalid setting with its line and the reason. It exits 0 only for a valid file, and `--json` gives the same answer as one
document. A key that is not on the list, an Alias or a Methodology that does not exist, no Validation command, and a limit above
the installation's are errors. The check stores nothing.

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
