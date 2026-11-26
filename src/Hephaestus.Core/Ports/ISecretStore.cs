using Hephaestus.Core.Secrets;

namespace Hephaestus.Core.Ports;

/// <summary>
/// Holds secret values outside the database (AD-16): a value is a file on the secrets volume, and the database records the
/// name. Names are listed through <see cref="IReadStore"/>. No method here returns or logs a value except
/// <see cref="ReadAsync"/>, which only the runtime that resolves a secret reference calls.
/// </summary>
public interface ISecretStore
{
    /// <summary>
    /// Stores a value under a name, replacing an earlier one. The name must satisfy <see cref="SecretPolicy.IsValidName"/> and
    /// the value <see cref="SecretPolicy.CheckValue"/>; otherwise <see cref="ArgumentException"/> is thrown, with a message
    /// that does not contain the value.
    /// </summary>
    Task SetAsync(string name, string value, CancellationToken cancellationToken);

    /// <summary>Removes the file and the name; returns false when no such secret was stored.</summary>
    Task<bool> RemoveAsync(string name, CancellationToken cancellationToken);

    /// <summary>The stored value, or null when there is none.</summary>
    Task<string?> ReadAsync(string name, CancellationToken cancellationToken);
}
