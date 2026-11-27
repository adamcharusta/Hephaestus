using Hephaestus.Core.Trust;

namespace Hephaestus.Core.Tests.Trust;

/// <summary>AD-15: one function decides, by numeric id, whether the actor of a Signal is a Trusted User.</summary>
public class AdmissionTests
{
    private static readonly HashSet<long> Trusted = [29, 31];

    [Fact]
    public void A_Trusted_User_is_admitted_by_id()
    {
        Assert.Equal(Admitted.Trusted, Admission.Decide(29, "alice", Trusted, "hephaestus-bot[bot]"));
    }

    [Fact]
    public void The_login_decides_nothing_so_a_login_that_changed_hands_is_not_trusted()
    {
        Assert.Equal(Admitted.NotTrusted, Admission.Decide(500, "alice", Trusted, null));
        Assert.Equal(Admitted.Trusted, Admission.Decide(29, "somebody-else-now", Trusted, null));
    }

    [Fact]
    public void An_actor_who_is_not_in_the_list_is_not_trusted()
    {
        Assert.Equal(Admitted.NotTrusted, Admission.Decide(77, "mallory", Trusted, null));
        Assert.Equal(Admitted.NotTrusted, Admission.Decide(77, "mallory", new HashSet<long>(), null));
    }

    [Fact]
    public void An_event_with_no_actor_is_trusted_for_nobody()
    {
        Assert.Equal(Admitted.NoActor, Admission.Decide(null, null, Trusted, null));
    }

    [Theory]
    [InlineData("hephaestus-bot[bot]")]
    [InlineData("Hephaestus-Bot[BOT]")]
    public void Hephaestus_own_bot_account_is_never_admitted_even_when_its_id_is_in_the_list(string login)
    {
        Assert.Equal(Admitted.OwnIdentity, Admission.Decide(29, login, Trusted, "hephaestus-bot[bot]"));
        Assert.Equal(Admitted.OwnIdentity, Admission.Decide(null, login, Trusted, "hephaestus-bot[bot]"));
    }

    [Fact]
    public void Another_apps_bot_account_is_an_ordinary_actor()
    {
        Assert.Equal(Admitted.NotTrusted, Admission.Decide(77, "dependabot[bot]", Trusted, "hephaestus-bot[bot]"));
        Assert.Equal(Admitted.Trusted, Admission.Decide(29, "dependabot[bot]", Trusted, "hephaestus-bot[bot]"));
    }
}
