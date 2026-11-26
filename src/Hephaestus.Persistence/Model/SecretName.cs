namespace Hephaestus.Persistence.Model;

/// <summary>The name of a stored secret. The value is a file on the secrets volume and is never part of the database (AD-16).</summary>
internal sealed class SecretName
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}
