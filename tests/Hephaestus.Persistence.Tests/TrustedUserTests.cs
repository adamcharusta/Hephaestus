using Hephaestus.Core.Trust;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hephaestus.Persistence.Tests;

/// <summary>Story 1.8, AD-15, FR-3: a Trusted User is a numeric id; the login is a display column; the list outlives a restart.</summary>
public class TrustedUserTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Nobody_is_trusted_until_someone_is_added()
    {
        using var installation = new TempInstallation();
        await installation.MigrateAsync();

        Assert.Empty(await installation.Reads.ListTrustedUsersAsync(Token));
    }

    [Fact]
    public async Task An_added_user_is_listed_by_id_with_the_login_and_the_time_they_were_added()
    {
        using var installation = new TempInstallation(new FixedTimeProvider(Noon));
        await installation.MigrateAsync();

        var written = await installation.Configuration.AddTrustedUserAsync(583231, "Alice", Token);

        Assert.True(written.Added);
        var listed = Assert.Single(await installation.Reads.ListTrustedUsersAsync(Token));
        Assert.Equal(new TrustedUser(583231, "Alice", Noon), listed);
        Assert.Equal(listed, written.User);
    }

    [Fact]
    public async Task Adding_the_same_id_again_keeps_one_row_and_its_time_and_refreshes_only_the_login()
    {
        var clock = new MovingTime(Noon);
        using var installation = new TempInstallation(clock);
        await installation.MigrateAsync();
        await installation.Configuration.AddTrustedUserAsync(583231, "alice", Token);
        clock.Now = Noon.AddDays(3);

        var again = await installation.Configuration.AddTrustedUserAsync(583231, "alice-renamed", Token);

        Assert.False(again.Added);
        Assert.Equal(new TrustedUser(583231, "alice-renamed", Noon), again.User);
        Assert.Equal([new TrustedUser(583231, "alice-renamed", Noon)], await installation.Reads.ListTrustedUsersAsync(Token));
    }

    [Fact]
    public async Task Two_users_may_hold_one_login_because_a_stored_login_can_be_stale()
    {
        using var installation = new TempInstallation();
        await installation.MigrateAsync();

        await installation.Configuration.AddTrustedUserAsync(1, "alice", Token);
        await installation.Configuration.AddTrustedUserAsync(2, "alice", Token);

        Assert.Equal([1L, 2L], (await installation.Reads.ListTrustedUsersAsync(Token)).Select(u => u.UserId));
    }

    [Fact]
    public async Task The_list_is_in_login_order_ignoring_case_then_by_id()
    {
        using var installation = new TempInstallation();
        await installation.MigrateAsync();

        await installation.Configuration.AddTrustedUserAsync(5, "bob", Token);
        await installation.Configuration.AddTrustedUserAsync(4, "Carol", Token);
        await installation.Configuration.AddTrustedUserAsync(3, "alice", Token);
        await installation.Configuration.AddTrustedUserAsync(2, "Alice", Token);

        Assert.Equal([2L, 3L, 5L, 4L], (await installation.Reads.ListTrustedUsersAsync(Token)).Select(u => u.UserId));
    }

    [Fact]
    public async Task A_removed_user_is_gone_and_removing_again_says_nobody_was_removed()
    {
        using var installation = new TempInstallation();
        await installation.MigrateAsync();
        await installation.Configuration.AddTrustedUserAsync(1, "alice", Token);
        await installation.Configuration.AddTrustedUserAsync(2, "bob", Token);

        Assert.True(await installation.Configuration.RemoveTrustedUserAsync(1, Token));
        Assert.False(await installation.Configuration.RemoveTrustedUserAsync(1, Token));

        Assert.Equal([2L], (await installation.Reads.ListTrustedUsersAsync(Token)).Select(u => u.UserId));
    }

    [Fact]
    public async Task Many_adds_of_one_id_at_once_leave_one_row_and_none_fails()
    {
        using var installation = new TempInstallation();
        await installation.MigrateAsync();

        var writes = await Task.WhenAll(Enumerable.Range(1, 20).Select(n => installation.Configuration.AddTrustedUserAsync(7, $"login-{n}", Token)));

        Assert.Equal(1, writes.Count(w => w.Added));
        Assert.Single(await installation.Reads.ListTrustedUsersAsync(Token));
    }

    [Fact]
    public async Task Adds_and_removals_of_one_id_at_once_never_fail_whatever_order_they_fall_in()
    {
        using var installation = new TempInstallation();
        await installation.MigrateAsync();

        var work = Enumerable.Range(1, 40).Select<int, Task>(n => n % 2 == 0
            ? Task.Run(async () => await installation.Configuration.AddTrustedUserAsync(7, $"login-{n}", Token), Token)
            : Task.Run(async () => await installation.Configuration.RemoveTrustedUserAsync(7, Token), Token));
        await Task.WhenAll(work);

        Assert.InRange((await installation.Reads.ListTrustedUsersAsync(Token)).Count, 0, 1);
    }

    [Fact]
    public async Task The_users_are_unchanged_after_a_restart()
    {
        using var installation = new TempInstallation(new FixedTimeProvider(Noon));
        await installation.MigrateAsync();
        await installation.Configuration.AddTrustedUserAsync(583231, "alice", Token);
        await installation.Configuration.AddTrustedUserAsync(1001, "bob", Token);
        var before = await installation.Reads.ListTrustedUsersAsync(Token);

        await using var restarted = installation.Restart();

        Assert.Equal(before, await restarted.GetRequiredService<Hephaestus.Core.Ports.IReadStore>().ListTrustedUsersAsync(Token));
    }

    [Fact]
    public async Task The_login_column_holds_the_longest_login_and_the_id_is_the_key()
    {
        using var installation = new TempInstallation();
        await installation.MigrateAsync();
        var longest = new string('a', GitHubLogin.MaximumLength) + GitHubLogin.BotSuffix;

        await installation.Configuration.AddTrustedUserAsync(1, longest, Token);

        await using var context = installation.Database.OpenReader();
        Assert.Equal(longest, (await context.TrustedUsers.SingleAsync(Token)).Login);
        Assert.Equal(["UserId"], context.Model.FindEntityType(typeof(Model.TrustedUserEntry))!.FindPrimaryKey()!.Properties.Select(p => p.Name));
    }

    [Fact]
    public async Task The_model_and_the_migrations_agree()
    {
        using var installation = new TempInstallation();
        await installation.MigrateAsync();

        await using var context = installation.Database.OpenWriter();

        Assert.False(context.Database.HasPendingModelChanges());
    }

    private sealed class MovingTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
