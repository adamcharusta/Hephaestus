using System.Text.RegularExpressions;
using Hephaestus.Core.Secrets;

namespace Hephaestus.Core.Invocations;

/// <summary>
/// A secret an invocation needs, by name and by where it goes (AD-10): never the value. Only the runtime resolves it, through
/// <c>ISecretStore</c>, when it starts the step container (AD-16).
/// </summary>
public sealed partial record SecretReference
{
    public SecretReference(string name, string target)
    {
        if (!SecretPolicy.IsValidName(name))
        {
            throw new ArgumentException($"'{name}' is not a valid secret name.", nameof(name));
        }

        if (!IsValidTarget(target))
        {
            throw new ArgumentException($"'{target}' is neither an environment variable name nor an absolute file path.", nameof(target));
        }

        Name = name;
        Target = target;
    }

    /// <summary>The name the secret is stored under.</summary>
    public string Name { get; }

    /// <summary>The environment variable the value is placed in, or an absolute path of the container's file system it is written to.</summary>
    public string Target { get; }

    public bool IsEnvironmentVariable => !Target.StartsWith('/');

    public static bool IsValidTarget(string? target) =>
        target is { Length: > 0 and <= 256 } && (VariableName().IsMatch(target) || (target.StartsWith('/') && !target.Any(char.IsControl) && !target.Split('/').Contains("..")));

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex VariableName();
}
