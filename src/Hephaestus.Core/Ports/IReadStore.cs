namespace Hephaestus.Core.Ports;

/// <summary>
/// Every read of stored data goes through here (AD-17). Persistence implements it on a read-only SQLite connection, so a read
/// can never change anything and no endpoint depends on <c>IUnitOfWork</c>.
/// </summary>
public interface IReadStore
{
    /// <summary>The names of the stored secrets in ascending order, and nothing else about them.</summary>
    Task<IReadOnlyList<string>> ListSecretNamesAsync(CancellationToken cancellationToken);
}
