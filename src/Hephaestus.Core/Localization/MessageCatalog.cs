using System.Globalization;
using System.Reflection;
using System.Resources;

namespace Hephaestus.Core.Localization;

/// <summary>Resolves a <see cref="Message"/> into a sentence from the embedded <c>en</c> and <c>pl</c> resources (AD-21).</summary>
public sealed class MessageCatalog
{
    // One resource set per language (Messages.en.resx, Messages.pl.resx), looked up by name rather than by culture: the
    // host runs in globalization-invariant mode, which cannot create the Polish culture.
    private static readonly IReadOnlyDictionary<Language, ResourceManager> Resources = new Dictionary<Language, ResourceManager>
    {
        [Language.English] = new("Hephaestus.Core.Localization.Messages.en", typeof(MessageCatalog).Assembly),
        [Language.Polish] = new("Hephaestus.Core.Localization.Messages.pl", typeof(MessageCatalog).Assembly),
    };

    public static MessageCatalog Instance { get; } = new();

    /// <summary>An argument that is itself a <see cref="Message"/> is resolved in the same language first, so that a sentence can carry the reason another sentence gives.</summary>
    public string Resolve(Message message, Language language)
    {
        var template = Template(message.Id, language)
            ?? throw new KeyNotFoundException($"Message id '{message.Id}' has no entry in language '{language.Tag()}'.");
        return message.Arguments is not { Length: > 0 }
            ? template
            : string.Format(CultureInfo.InvariantCulture, template, message.Arguments.Select(argument => argument is Message inner ? Resolve(inner, language) : argument).ToArray());
    }

    public string Resolve(string id, Language language) => Resolve(new Message(id), language);

    /// <summary>The ids that have an entry in a language, without falling back to another one.</summary>
    public IReadOnlySet<string> Ids(Language language)
    {
        var set = Resources[language].GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false);
        return set is null
            ? new HashSet<string>()
            : set.Cast<System.Collections.DictionaryEntry>().Select(e => (string)e.Key).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Every id declared in <see cref="MessageIds"/>.</summary>
    public static IReadOnlyList<string> DeclaredIds { get; } = typeof(MessageIds)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.IsLiteral)
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToArray();

    private static string? Template(string id, Language language)
    {
        var set = Resources[language].GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false);
        return set?.GetString(id);
    }
}
