# Create and connect Hephaestus's GitHub App

Hephaestus acts on GitHub as its own [GitHub App](https://docs.github.com/en/apps/overview), not as you. This page leads
through creating the App with exactly the permissions Hephaestus needs, installing it on the repositories you choose, and
connecting it. It is a required step of installation: nothing Hephaestus does on GitHub works before it.

## Why a separate identity

- **You can approve its pull requests.** GitHub does not let an author approve their own pull request. If Hephaestus used
  your account or a personal access token, every pull request it opens would be yours, and you could not be the independent
  reviewer who approves it.
- **It touches only what you allow.** An App has the permissions you list when you create it, and reaches only the
  repositories you install it on. A personal access token reaches everything your account does.
- **You can see and revoke it on its own.** What the App does is attributed to the App. You can suspend, uninstall, or delete
  it, or revoke its private key, without touching your account.

## 1. Create the App

On GitHub, open **Settings → Developer settings → GitHub Apps → New GitHub App** (for an organization, the organization's
settings). Then:

| Field | Value |
| --- | --- |
| GitHub App name | Any name you like, for example `Hephaestus (yourname)`. It must be unique on GitHub. |
| Homepage URL | Any URL, for example this repository's. |
| Webhook | Clear **Active**. Hephaestus polls GitHub; it needs no webhook. |
| Where can this GitHub App be installed? | **Only on this account.** |

Under **Repository permissions**, set exactly these, and nothing else:

| Permission | Access |
| --- | --- |
| Contents | Read and write |
| Issues | Read and write |
| Pull requests | Read and write |
| Metadata | Read-only (GitHub selects it for you) |
| Checks | Read-only |
| Commit statuses | Read-only |

Leave **Organization permissions** and **Account permissions** empty, and subscribe to no events.

**Never grant `Workflows` or `Administration`.** Hephaestus does not need them. An App that can write workflow files could
make a pushed branch run code of its choosing with your repository's secrets, and one with administration could change the
repository's settings. `github check` warns when either is granted.

Choose **Create GitHub App**. The App's settings page shows its **App ID**, a number; keep it.

## 2. Generate a private key

On the App's settings page, under **Private keys**, choose **Generate a private key**. GitHub downloads a `.pem` file once.
This file is the App's credential: anyone who holds it can act as the App on every repository it is installed on. Keep it
out of version control and chat, and delete your copy once Hephaestus holds it.

## 3. Install the App on your repositories

On the App's page choose **Install App**, select your account or organization, and choose **Only select repositories**. Pick
the repositories Hephaestus is to work on. Install it on no others. You can change the selection later.

## 4. Connect it to Hephaestus

Give the App ID with `--app-id` and the key on standard input, with `-i` so that it reaches the command:

```bash
docker exec -i hephaestus hephaestus github connect --app-id 123456 < hephaestus.pem
```

A key given as an argument is refused, and nothing is stored. Hephaestus signs in as the App, and only when GitHub accepts
the App ID and the key does it store them: the key as the secret `github-app-private-key` on the secrets volume (never in
the database), the App ID in the database. The command then prints the App, the repositories it is installed on, and each
required permission as present or missing:

```text
Connected the GitHub App.
GitHub App: Hephaestus (yourname) (hephaestus-yourname), id 123456, owned by yourname

Installed on:
  yourname (User): selected repositories
    yourname/example

Permissions:
  Permission     Needs  Has    Status
  contents       write  write  present
  issues         write  write  present
  pull-requests  write  write  present
  metadata       read   read   present
  checks         read   read   present
  statuses       read   read   present

All required permissions are present.
```

The **Has** column is the lowest level that the App and every one of its installations agree on, so a permission shows as
missing until each installation has accepted it. A forbidden permission is warned about if the App or any installation holds it.

Run `docker exec hephaestus hephaestus github check` at any time to read the same report again with the stored credential.
Add `--json` to either command for one JSON document; its `ready` field is `true` when every required permission is present.
The exit code says whether GitHub answered, not whether the permissions are enough: a report with a missing permission still
exits with 0, so a script should read `ready`.

## When it fails

Each failure exits with 1, prints a title and a detail on standard error, and changes nothing that was stored before.

| Title | What it means | What to do |
| --- | --- | --- |
| GitHub is unreachable | GitHub did not answer in time, answered with a server error, or is limiting requests. | Check the host's network and try again later. |
| GitHub credential rejected | GitHub refused the App ID and key, or the key is not a PEM-encoded RSA private key (then GitHub is not asked). | Check that the ID belongs to the App, that the key was generated for it and not revoked, and that you gave the `.pem` file. The host's clock must be right to within a minute: GitHub refuses a signed request from a clock that is off. |
| No GitHub App is connected | `github check` found no stored App ID and key. | Run `github connect`. |

Without `-i`, or without input piped in, `docker exec` gives the command an empty standard input, and the command refuses it.

## Changing permissions later

When you change an App's permissions on GitHub, each installation must accept the change before it takes effect. Accept it
from the notification GitHub sends to the account or organization that installed the App, then run `github check`.

## Replacing the private key

Generate a new key on the App's settings page, run `github connect` again with the new file, and then delete the old key on
GitHub. The new key replaces the stored one only when GitHub has accepted it.

## Your repositories' workflows

A pushed branch named `hephaestus/...` can start the repository's existing GitHub Actions workflows, and they run code an
Agent wrote. Make sure that workflows triggered by those branches and their pull requests have no access to deployment or
publishing secrets and use a read-only token. Hephaestus's own protection against this is in the
[threat model](security/threat-model.md) (T23); it does not stop an existing workflow from running.
