using Hephaestus.Core.Identity;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Hephaestus.Persistence.Model;

/// <summary>Entity ids are UUID version 7, stored as lowercase text (AR-30). A Guid of another version is refused on write.</summary>
internal sealed class EntityIdConverter() : ValueConverter<Guid, string>(id => ToText(id), text => Guid.ParseExact(text, "D"))
{
    public static string ToText(Guid id) => EntityId.IsValid(id)
        ? id.ToString("D")
        : throw new InvalidOperationException("An entity id must be a UUID version 7.");
}
