using Hephaestus.Core.Localization;
using Hephaestus.Core.Settings;

namespace Hephaestus.Core.Tests.Settings;

/// <summary>Story 1.9, FR-69: the installation's language is a setting that is `en` or `pl`; a new installation is in English.</summary>
public class SettingServiceTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_new_installation_has_the_language_setting_at_its_default_English()
    {
        var service = new SettingService(new FakeInstallation(), new FakeInstallation());

        var setting = Assert.Single(await service.ListAsync(Token));

        Assert.Equal(new SettingValue("language", "en", "en", IsSet: false), setting);
    }

    [Theory]
    [InlineData("pl", "pl")]
    [InlineData("en", "en")]
    [InlineData(" PL ", "pl")]
    public async Task The_language_is_stored_in_its_tag_and_listed(string value, string stored)
    {
        var store = new FakeInstallation();
        var service = new SettingService(store, store);

        var result = await service.SetAsync("language", value, Token);

        Assert.Equal(SettingFailure.None, result.Failure);
        Assert.Equal(new SettingValue("language", stored, "en", IsSet: true), result.Setting);
        Assert.Equal(stored, store.Settings["language"]);
        Assert.Equal(stored, Assert.Single(await service.ListAsync(Token)).Value);
    }

    [Theory]
    [InlineData("tlh")]
    [InlineData("polish")]
    [InlineData("")]
    [InlineData("pl-PL")]
    public async Task Any_other_language_is_refused_and_the_stored_one_stays(string value)
    {
        var store = new FakeInstallation();
        store.Settings["language"] = "pl";
        var service = new SettingService(store, store);

        var result = await service.SetAsync("language", value, Token);

        Assert.Equal(SettingFailure.InvalidValue, result.Failure);
        Assert.Equal("pl", store.Settings["language"]);
    }

    [Fact]
    public async Task A_name_that_is_not_a_setting_is_refused()
    {
        var store = new FakeInstallation();
        var service = new SettingService(store, store);

        var result = await service.SetAsync("colour", "blue", Token);

        Assert.Equal(SettingFailure.UnknownSetting, result.Failure);
        Assert.Empty(store.Settings);
    }

    [Fact]
    public async Task A_stored_value_that_is_no_longer_valid_reads_as_the_default()
    {
        var store = new FakeInstallation();
        store.Settings["language"] = "tlh";
        var service = new SettingService(store, store);

        var setting = Assert.Single(await service.ListAsync(Token));
        Assert.Equal("en", setting.Value);
        Assert.False(setting.IsSet);
        Assert.Null(InstallationSettings.StoredLanguage(store.Settings));
    }

    [Fact]
    public void The_stored_language_wins_and_the_fallback_serves_a_new_installation()
    {
        Assert.Equal(Language.Polish, LanguageResolver.Installation(Language.Polish, Language.English));
        Assert.Equal(Language.English, LanguageResolver.Installation(Language.English, Language.Polish));
        Assert.Equal(Language.Polish, LanguageResolver.Installation(null, Language.Polish));
        Assert.Equal(Language.Polish, InstallationSettings.StoredLanguage(new Dictionary<string, string> { ["language"] = "pl" }));
        Assert.Null(InstallationSettings.StoredLanguage(new Dictionary<string, string>()));
    }

    [Theory]
    [InlineData("pl,en;q=0.5", Language.Polish)]
    [InlineData("en-GB", Language.English)]
    [InlineData("de, pl", Language.Polish)]
    public void A_header_that_names_a_supported_language_decides(string header, Language expected)
    {
        Assert.Equal(expected, LanguageResolver.FromHeader(header));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("de, fr;q=0.8")]
    public void A_header_that_names_none_decides_nothing(string? header)
    {
        Assert.Null(LanguageResolver.FromHeader(header));
    }

    [Fact]
    public void The_setting_tells_the_owner_which_values_it_accepts()
    {
        Assert.Equal("en, pl", InstallationSettings.Language.Accepts);
        Assert.Same(InstallationSettings.Language, InstallationSettings.Find("language"));
        Assert.Null(InstallationSettings.Find("nope"));
        Assert.Null(InstallationSettings.Find(null));
    }
}
