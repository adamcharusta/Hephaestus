namespace Hephaestus.Core.Identity;

/// <summary>Entity ids are UUID version 7, stored as text (AR-30); the time inside one comes from <see cref="TimeProvider"/>.</summary>
public static class EntityId
{
    public static Guid New(TimeProvider time) => Guid.CreateVersion7(time.GetUtcNow());

    public static bool IsValid(Guid id) => id.Version == 7;
}
