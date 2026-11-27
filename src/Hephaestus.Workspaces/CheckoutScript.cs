namespace Hephaestus.Workspaces;

/// <summary>
/// What the checkout container runs (AD-6, AD-9, AR-49), as <c>sh -c</c>: POSIX shell and git, because the <c>hephaestus-tools</c> image holds nothing else. The workspace and the bundle are its
/// two arguments, and the branch and the tip it must find are in its environment, so that no name is ever part of the script's text. Nothing of an earlier step survives: the directory is emptied,
/// dot files and unreadable directories included, and a repository is made in it from an empty template, so it has no hook, the default configuration, no remote, and no untracked or ignored file.
/// </summary>
internal static class CheckoutScript
{
    /// <summary>The exit codes the script ends with when the bundle is not what it was told: it does not verify, it has not exactly one head, or its head is not the tip the runtime named.</summary>
    public const int BundleNotVerified = 21;
    public const int BundleHeads = 22;
    public const int BundleTip = 23;

    public const string Text = Preparation + VerifyBundle + Checkout;

    /// <summary>The shell the checkout and the checkpoint share: it ends the script, with one of the three codes above, unless the bundle verifies, carries exactly one head, and carries the tip named in <c>HEPHAESTUS_TIP</c>. It leaves the head's reference in <c>$ref</c>.</summary>
    public const string VerifyBundle = """
        git bundle verify "$bundle" > /dev/null 2>&1 || exit 21
        heads="$(git bundle list-heads "$bundle")"
        case "$heads" in
          "" | *"
        "*) exit 22 ;;
        esac
        [ "${heads%% *}" = "$HEPHAESTUS_TIP" ] || exit 23
        ref="${heads#* }"

        """;

    private const string Preparation = """
        set -eu
        workspace="$1"
        bundle="$2"
        export GIT_CONFIG_NOSYSTEM=1 GIT_CONFIG_GLOBAL=/dev/null GIT_TERMINAL_PROMPT=0
        umask 022
        cd /
        chmod u+rwx "$workspace"
        find "$workspace" -mindepth 1 -maxdepth 1 ! -type l -exec chmod -R u+rwX {} \; 2> /dev/null || true
        find "$workspace" -mindepth 1 -maxdepth 1 -exec rm -rf {} \;
        git init --quiet --template= "$workspace"
        cd "$workspace"

        """;

    private const string Checkout = """
        git fetch --quiet --no-tags "$bundle" "+$ref:refs/heads/$HEPHAESTUS_BRANCH"
        git symbolic-ref HEAD "refs/heads/$HEPHAESTUS_BRANCH"
        git reset --quiet --hard
        chmod -R u+rwX "$workspace"
        """;
}
