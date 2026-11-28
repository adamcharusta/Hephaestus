using Hephaestus.Core.Settings;

namespace Hephaestus.Core.Tests.Settings;

/// <summary>NFR-10, AD-11: the step retry bound is an installation setting, two by default, from none to ten.</summary>
public class StepRetriesSettingTests
{
    [Theory]
    [InlineData("0", "0")]
    [InlineData("2", "2")]
    [InlineData("10", "10")]
    public void A_whole_number_from_none_to_ten_is_accepted(string text, string stored)
    {
        Assert.Equal(stored, InstallationSettings.StepRetries.Accept(text));
    }

    [Theory]
    [InlineData("11")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("")]
    [InlineData("two")]
    [InlineData("002")]
    [InlineData("٢")]
    public void Anything_else_is_not_a_value_of_the_setting(string text)
    {
        Assert.Null(InstallationSettings.StepRetries.Accept(text));
    }

    [Fact]
    public void The_setting_is_listed_with_its_default_and_its_range_and_found_by_name()
    {
        Assert.Equal(("stepRetries", "2", "0-10"), (InstallationSettings.StepRetries.Name, InstallationSettings.StepRetries.Default, InstallationSettings.StepRetries.Accepts));
        Assert.Same(InstallationSettings.StepRetries, InstallationSettings.Find("stepRetries"));
        Assert.Contains(InstallationSettings.StepRetries, InstallationSettings.All);
    }

    [Fact]
    public void The_bound_in_force_is_the_stored_value_or_the_default_when_none_was_stored_or_the_stored_one_is_not_a_value()
    {
        Assert.Equal(2, InstallationSettings.StepRetriesFrom(new Dictionary<string, string>()));
        Assert.Equal(5, InstallationSettings.StepRetriesFrom(new Dictionary<string, string> { ["stepRetries"] = "5" }));
        Assert.Equal(0, InstallationSettings.StepRetriesFrom(new Dictionary<string, string> { ["stepRetries"] = "0" }));
        Assert.Equal(2, InstallationSettings.StepRetriesFrom(new Dictionary<string, string> { ["stepRetries"] = "99" }));
        Assert.Equal(2, InstallationSettings.StepRetriesFrom(new Dictionary<string, string> { ["stepRetries"] = "many" }));
    }
}
