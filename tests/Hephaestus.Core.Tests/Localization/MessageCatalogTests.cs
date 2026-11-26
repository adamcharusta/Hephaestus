using System.Text.RegularExpressions;
using Hephaestus.Core.Localization;

namespace Hephaestus.Core.Tests.Localization;

/// <summary>AD-21: every sentence is a message id with an entry in each supported language, and a test fails when one is missing.</summary>
public partial class MessageCatalogTests
{
    private static readonly MessageCatalog Catalog = MessageCatalog.Instance;

    public static TheoryData<Language> SupportedLanguages => new() { Language.English, Language.Polish };

    [Theory]
    [MemberData(nameof(SupportedLanguages))]
    public void Every_declared_message_id_has_an_entry_in_the_language(Language language)
    {
        var missing = MessageCatalog.DeclaredIds.Except(Catalog.Ids(language)).Order(StringComparer.Ordinal).ToList();

        Assert.True(missing.Count == 0, $"Message ids with no {language.Tag()} entry: {string.Join(", ", missing)}");
    }

    [Theory]
    [MemberData(nameof(SupportedLanguages))]
    public void Every_resource_entry_is_a_declared_message_id(Language language)
    {
        var undeclared = Catalog.Ids(language).Except(MessageCatalog.DeclaredIds).Order(StringComparer.Ordinal).ToList();

        Assert.True(undeclared.Count == 0, $"{language.Tag()} entries that are not in MessageIds: {string.Join(", ", undeclared)}");
    }

    [Fact]
    public void Both_languages_take_the_same_parameters_for_every_id()
    {
        foreach (var id in MessageCatalog.DeclaredIds)
        {
            Assert.Equal(Placeholders(Catalog.Resolve(id, Language.English)), Placeholders(Catalog.Resolve(id, Language.Polish)));
        }
    }

    [Fact]
    public void An_entry_is_never_empty()
    {
        foreach (var language in new[] { Language.English, Language.Polish })
        {
            foreach (var id in MessageCatalog.DeclaredIds)
            {
                Assert.False(string.IsNullOrWhiteSpace(Catalog.Resolve(id, language)), $"{id} is empty in {language.Tag()}.");
            }
        }
    }

    [Fact]
    public void A_sentence_differs_between_the_languages()
    {
        var english = Catalog.Resolve(new Message(MessageIds.VersionLine, "1.2.3"), Language.English);
        var polish = Catalog.Resolve(new Message(MessageIds.VersionLine, "1.2.3"), Language.Polish);

        Assert.Equal("Hephaestus version 1.2.3", english);
        Assert.Equal("Hephaestus w wersji 1.2.3", polish);
    }

    [Fact]
    public void Parameters_are_formatted_with_the_invariant_culture()
    {
        var text = Catalog.Resolve(new Message(MessageIds.ProblemFailedDetail, 1234567), Language.English);

        Assert.Equal("The API refused the request with status 1234567.", text);
    }

    [Fact]
    public void An_unknown_id_is_an_error_naming_it()
    {
        var exception = Assert.Throws<KeyNotFoundException>(() => Catalog.Resolve("no.such.id", Language.Polish));

        Assert.Contains("no.such.id", exception.Message, StringComparison.Ordinal);
        Assert.Contains("pl", exception.Message, StringComparison.Ordinal);
    }

    private static List<string> Placeholders(string template) =>
        PlaceholderPattern().Matches(template).Select(m => m.Value).Distinct().Order(StringComparer.Ordinal).ToList();

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex PlaceholderPattern();
}
