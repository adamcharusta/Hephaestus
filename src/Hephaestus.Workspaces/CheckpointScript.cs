namespace Hephaestus.Workspaces;

/// <summary>
/// What the checkpoint container runs (AD-9, FR-20, AR-50), as <c>sh -c</c>: POSIX shell and git. It never uses the git directory of <c>/workspace</c>: the repository it works in is one it makes itself, outside the Task volume, from the bundle of the
/// last pushed tip, and <c>GIT_DIR</c> and <c>GIT_WORK_TREE</c> are set so that git looks for no other, so no configuration, hook, or attribute driver an Agent left in <c>/workspace/.git</c> is read or run. The work tree is only read: the volume is
/// mounted read-only, and the index lives in the git directory. The tree is taken as it is, <c>.gitignore</c> respected, over the index of the tip's tree, so that a tracked file stays tracked and an Agent's own commits are flattened away; the commit is
/// made with the parent, the identity, and the message that Core composed, which arrive in the environment and never in the script's text. A tree equal to the tip's ends the script with <see cref="Unchanged"/> before any commit or bundle is made.
/// A file the step user cannot read is left out (<c>--ignore-errors</c>) and does not stop the checkpoint: the volume is read-only, so nothing can make it readable, and keeping the rest is better than keeping nothing.
/// </summary>
internal static class CheckpointScript
{
    /// <summary>The exit code of a checkpoint that found the tree unchanged: no commit and no bundle exist.</summary>
    public const int Unchanged = 30;

    /// <summary>The exit code of a checkpoint whose index would hold a repository the Agent made inside the workspace as a link to a commit: it could not be left out, and the checkpoint stops rather than commit what nobody can fetch.</summary>
    public const int NestedRepository = 31;

    public const string Text = Preparation + CheckoutScript.VerifyBundle + Commit;

    private const string Preparation = """
        set -eu
        workspace="$1"
        bundle="$2"
        gitdir="$3"
        result="$4"
        export GIT_CONFIG_NOSYSTEM=1 GIT_CONFIG_GLOBAL=/dev/null GIT_TERMINAL_PROMPT=0 GIT_LITERAL_PATHSPECS=1
        export GIT_CONFIG_COUNT=2 GIT_CONFIG_KEY_0=gc.auto GIT_CONFIG_VALUE_0=0 GIT_CONFIG_KEY_1=maintenance.auto GIT_CONFIG_VALUE_1=false
        umask 022
        cd /
        rm -rf "$gitdir" "$result"
        git init --quiet --bare --template= "$gitdir"
        export GIT_DIR="$gitdir" GIT_WORK_TREE="$workspace"

        """;

    private const string Commit = """
        git fetch --quiet --no-tags "$bundle" "+$ref:refs/heads/$HEPHAESTUS_BRANCH"
        cd "$workspace"
        git read-tree "$HEPHAESTUS_TIP"
        git add --all --ignore-errors || true
        find . -path ./.git -prune -o -name .git -prune -exec sh -c 'p="${1#./}"; p="${p%/.git}"; if git ls-tree "$HEPHAESTUS_TIP" -- "$p" | grep -q "^160000"; then git reset --quiet "$HEPHAESTUS_TIP" -- "$p"; else git rm --cached --quiet --force --ignore-unmatch -- "$p"; fi' sh {} \; 2> /dev/null || true
        if git diff-index --cached "$HEPHAESTUS_TIP" | grep -q "^:000000 160000 "; then
          echo "a repository inside the workspace could not be left out of the commit" >&2
          exit 31
        fi
        tree="$(git write-tree)"
        [ "$tree" != "$(git rev-parse "$HEPHAESTUS_TIP^{tree}")" ] || exit 30
        commit="$(printf '%s' "$HEPHAESTUS_MESSAGE" | GIT_AUTHOR_NAME="$HEPHAESTUS_NAME" GIT_AUTHOR_EMAIL="$HEPHAESTUS_EMAIL" GIT_COMMITTER_NAME="$HEPHAESTUS_NAME" GIT_COMMITTER_EMAIL="$HEPHAESTUS_EMAIL" git commit-tree "$tree" -p "$HEPHAESTUS_TIP" -F -)"
        git update-ref "refs/heads/$HEPHAESTUS_BRANCH" "$commit" "$HEPHAESTUS_TIP"
        cd /
        git bundle create --quiet "$result" "refs/heads/$HEPHAESTUS_BRANCH" "^$HEPHAESTUS_TIP"
        """;
}
