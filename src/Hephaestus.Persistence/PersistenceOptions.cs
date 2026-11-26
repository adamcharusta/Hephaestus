namespace Hephaestus.Persistence;

/// <summary>
/// Where the installation keeps its state (AR-39): the data volume holds the SQLite file, the secrets volume holds one file
/// per secret. The two are separate directories because the secrets volume is mounted into the <c>hephaestus</c> container
/// alone and backed up apart from the database.
/// </summary>
public sealed record PersistenceOptions(string DataDirectory, string SecretsDirectory)
{
    public const string DatabaseFileName = "hephaestus.db";

    /// <summary>How long a statement waits for a lock before it fails, in milliseconds.</summary>
    public const int BusyTimeoutMilliseconds = 5_000;

    public string DatabasePath => Path.Combine(DataDirectory, DatabaseFileName);
}
