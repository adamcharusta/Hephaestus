# `hephaestus.yml`: a Project's configuration

`hephaestus.yml` tells Hephaestus how to work on one repository: which image its Tasks run in, which commands decide whether the
work is acceptable, and the defaults for Agents, Methodology, and limits. It is a file of the repository itself, so a change to it
is a pull request that someone reviews and merges.

- It lives at the root of the repository's **Base Branch**, the branch you named in `project add`. Hephaestus reads it from that
  branch and from no other: the same file on a feature branch is never read. It reads the branch at its latest commit, and a check
  says which commit that was.
- No Task exists yet; when Tasks arrive, the file will be read again whenever a Task starts, and the settings it had then will stay
  with that Task until the Task ends, so a change merged while a Task runs applies to the next one.
- It is a request, not a setting of the installation: `project check` reports a limit above the Owner's own limit as an error. An
  installation limit the Owner has not set (`limit show` prints `none`) is no ceiling, so any well-formed value passes the check then.
- It is not the place for secrets, Trusted Users, Aliases, or limits of the installation; those belong to the Owner and are set with
  the command line (see the [README](../README.md)).

## Check it

```bash
docker exec hephaestus hephaestus project check acme/shop
```

`project check` reads `hephaestus.yml` from the Base Branch of the registered Project and says one of three things:

- **valid**, with the commit it was read from and the settings it holds (exit code 0);
- **missing**, when the Base Branch has no `hephaestus.yml` at its root (exit code 1);
- **not valid**, with a table that names **every** invalid setting, the line it is on, and the reason, not only the first (exit code 1).

Add `--json` for one JSON document, the same in every language (see below). The check changes nothing; run it as often as you
like, and again after the pull request that fixes the file is merged.

The JSON document has these members: `repositoryId`, `name`, `baseBranch`, `status` (`valid`, `invalid`, or `missing`), `commitId`,
`issues`, and `configuration`. Each issue is `{"setting", "line", "id", "arguments"}`: `setting` is the key (`limits.memory`,
`validation[1]`) or `hephaestus.yml` when the problem is the whole file, `line` is a number or `null`, and `id` with `arguments` is the reason
as a message id with its parameters; no sentence is in the document. `configuration` holds the settings of a valid file, with
`methodology` and `correctionRounds` at their defaults when the file does not name them and `null` for the others. The exit code is 0
only for `valid`, whether or not `--json` is given.

## The keys

Keys are written in camelCase, exactly as below. **Any other key, at any level, is an error that names it**: a misspelled key is
never ignored.

| Key | Required | Meaning |
| --- | --- | --- |
| `image` | yes | The image every step of a Task runs in, for example `node:22` or `mcr.microsoft.com/dotnet/sdk:10.0`. There is no Hephaestus base image, so the Project names its own. It needs Linux (glibc or musl) and `bash`; Hephaestus supplies the Agent CLIs and the certificates they need. |
| `validation` | yes | A list of commands, each run through `bash`; their exit status decides whether the work is acceptable. At least one is required, because Validation with no command would pass without checking anything. A Project with nothing to build states a command that says so, for example `echo nothing to build`. At most 20 commands, each at most 2000 characters. |
| `primaryAlias` | no | The Alias of the default Primary Agent with its Model, used by a Task that has no `primary:` Role Label. It must be an Alias that sets an Agent and a Model. |
| `fallbackAlias` | no | The Alias of the default Fallback Agent with its Model. It must be an Alias that sets an Agent and a Model, and it must not resolve to the same Agent as `primaryAlias`. |
| `effort` | no | The Alias of the default Effort, used by a Task that has no `effort:` Role Label. It must be an Alias that sets an Effort. |
| `methodology` | no | The Methodology the Project's Tasks follow. It must be one this installation ships (a directory of `methodologies/` with a `manifest.yml`). `default` when absent. |
| `correctionRounds` | no | The bound of the Correction Loop: the most rounds in which blocking Findings and failed Validation are returned to the implementing Agent. A whole number from 0 to 10; 2 when absent. |
| `limits` | no | The resources a Task's container may use, each at most the installation's own limit, which `limit show` prints. Its keys are below. A limit the file does not name is the installation's. |
| `limits.taskDuration` | no | The longest a Task may run, for example `90m` or `2h30m` (1 minute to 7 days). |
| `limits.cpus` | no | The most processors, for example `2` or `1.5` (0.1 to 1024). |
| `limits.memory` | no | The most memory, for example `512m` or `4g` (64m to 4096g). |
| `allowedEgressHosts` | no | A list of host names a Task's containers may reach, for example `api.nuget.org`. A host name, in lower or upper case, with no scheme, port, path, or wildcard, and not an address. The hosts an Agent's own CLI needs will be added by Hephaestus for the Agent's step and are never listed here. The list is applied by the egress proxy, which does not exist yet; with no list, the design lets any public host through. |
| `language` | no | `en` or `pl`: the language of the text Hephaestus writes on the Project's issues and pull requests. Without it, the installation's language is used. Any other value is an error. |

The value of an Alias key is the name the Owner gave the Alias with `alias add`; `alias list` shows them. An Alias that is not
defined on this installation is an error, and so is one of the wrong kind.

A build tool that does not honour the standard proxy variables, such as Maven or Gradle, needs its own proxy settings in the
Project's image; Hephaestus sets only the generic variables.

## What the check reports

Each problem names the setting (`limits.memory`, `validation[1]`, or `hephaestus.yml` for the whole file), the line when there is
one, and the reason:

- the file is larger than 64 KiB, is not UTF-8, is not YAML, holds more than one YAML document, is nested deeper than 8 levels, uses a
  YAML alias (`*name`, or a merge key `<<`), writes a key twice, or is not a mapping of settings;
- a key is not in the table above;
- `image` or `validation` is missing, an image is not an image reference, or a command is empty or holds a control character;
- an Alias is not defined, is the wrong kind, or the two Agents are the same;
- the Methodology is not available;
- a limit is malformed or above the installation's;
- the Correction Loop bound is not a whole number from 0 to 10;
- an egress host is not a host name;
- the language is not `en` or `pl`.

Text from the file that appears in a message is shortened and has control characters replaced, so a file cannot write into your
terminal.

## Examples

Each example is a complete `hephaestus.yml` that `project check` accepts when the Aliases it names are defined
(`alias add opus5.5 --agent claude-code --model claude-opus-5-5`, `alias add sol6.2 --agent codex-cli --model gpt-6.2`,
`alias add medium --effort medium`) and the installation's limits are at least what it asks for.

### .NET, with the stock .NET SDK image

```yaml
primaryAlias: opus5.5
fallbackAlias: sol6.2
effort: medium
methodology: default
validation:
  - dotnet build --nologo -warnaserror
  - dotnet test --nologo
correctionRounds: 2
image: mcr.microsoft.com/dotnet/sdk:10.0
limits:
  taskDuration: 90m
  cpus: 2
  memory: 4g
allowedEgressHosts:
  - api.nuget.org
language: en
```

### Node.js, with the stock Node image

```yaml
validation:
  - npm ci
  - npm run lint
  - npm test
image: node:22
limits:
  memory: 2g
allowedEgressHosts:
  - registry.npmjs.org
```

### Python, with the stock Python image

```yaml
primaryAlias: sol6.2
validation:
  - pip install --requirement requirements.txt
  - python -m pytest
correctionRounds: 3
image: python:3.13
allowedEgressHosts:
  - pypi.org
  - files.pythonhosted.org
```

### A repository with nothing to build

```yaml
validation:
  - echo nothing to build
image: alpine:3.22
```
