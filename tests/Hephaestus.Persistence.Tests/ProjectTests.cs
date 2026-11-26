using Hephaestus.Core.Ports;
using Hephaestus.Core.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hephaestus.Persistence.Tests;

/// <summary>Story 1.10, FR-1: a Project is stored by GitHub's repository id; removing it retires it and keeps the record; the list outlives a restart.</summary>
public class ProjectTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed class MovingTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static async Task<TempInstallation> StartAsync(TimeProvider? time = null)
    {
        var installation = new TempInstallation(time);
        await installation.MigrateAsync();
        return installation;
    }

    [Fact]
    public async Task A_new_installation_has_no_Project()
    {
        using var installation = await StartAsync();

        Assert.Empty(await installation.Reads.ListProjectsAsync(includeRemoved: true, Token));
    }

    [Fact]
    public async Task A_registered_Project_is_stored_by_its_repository_id_with_its_name_Base_Branch_and_time()
    {
        using var installation = await StartAsync(new FixedTimeProvider(Noon));

        var written = await installation.Configuration.RegisterProjectAsync(101, "acme/shop", "main", Token);

        Assert.Equal(ProjectChange.Registered, written.Change);
        var expected = new Project(101, "acme/shop", "main", Noon, null);
        Assert.Equal(expected, written.Project);
        Assert.Equal([expected], await installation.Reads.ListProjectsAsync(includeRemoved: false, Token));
    }

    [Fact]
    public async Task Registering_the_same_Project_again_changes_nothing()
    {
        var clock = new MovingTime(Noon);
        using var installation = await StartAsync(clock);
        await installation.Configuration.RegisterProjectAsync(101, "acme/shop", "main", Token);
        clock.Now = Noon.AddDays(2);

        var again = await installation.Configuration.RegisterProjectAsync(101, "acme/shop", "main", Token);

        Assert.Equal(ProjectChange.Unchanged, again.Change);
        Assert.Equal(new Project(101, "acme/shop", "main", Noon, null), again.Project);
        Assert.Single(await installation.Reads.ListProjectsAsync(includeRemoved: true, Token));
    }

    [Fact]
    public async Task A_renamed_repository_is_the_same_row_under_the_same_id_with_the_new_name_and_the_first_time()
    {
        var clock = new MovingTime(Noon);
        using var installation = await StartAsync(clock);
        await installation.Configuration.RegisterProjectAsync(101, "acme/shop", "main", Token);
        clock.Now = Noon.AddDays(2);

        var renamed = await installation.Configuration.RegisterProjectAsync(101, "acme/store", "main", Token);

        Assert.Equal(ProjectChange.Updated, renamed.Change);
        Assert.Equal([new Project(101, "acme/store", "main", Noon, null)], await installation.Reads.ListProjectsAsync(includeRemoved: false, Token));
    }

    [Fact]
    public async Task Another_Base_Branch_replaces_the_stored_one()
    {
        using var installation = await StartAsync();
        await installation.Configuration.RegisterProjectAsync(101, "acme/shop", "main", Token);

        var changed = await installation.Configuration.RegisterProjectAsync(101, "acme/shop", "develop", Token);

        Assert.Equal(ProjectChange.Updated, changed.Change);
        Assert.Equal("develop", Assert.Single(await installation.Reads.ListProjectsAsync(includeRemoved: false, Token)).BaseBranch);
    }

    [Fact]
    public async Task A_removed_Project_is_not_among_the_Projects_but_its_record_is_retained()
    {
        var clock = new MovingTime(Noon);
        using var installation = await StartAsync(clock);
        await installation.Configuration.RegisterProjectAsync(101, "acme/shop", "main", Token);
        clock.Now = Noon.AddHours(5);

        var removed = await installation.Configuration.RemoveProjectAsync(101, Token);

        Assert.Equal(new Project(101, "acme/shop", "main", Noon, Noon.AddHours(5)), removed);
        Assert.Empty(await installation.Reads.ListProjectsAsync(includeRemoved: false, Token));
        Assert.Equal([removed!], await installation.Reads.ListProjectsAsync(includeRemoved: true, Token));
        await using var context = installation.Database.OpenReader();
        Assert.Equal(1, await context.Projects.CountAsync(Token));
    }

    [Fact]
    public async Task Removing_an_unknown_or_an_already_removed_Project_removes_nothing()
    {
        var clock = new MovingTime(Noon);
        using var installation = await StartAsync(clock);
        await installation.Configuration.RegisterProjectAsync(101, "acme/shop", "main", Token);
        await installation.Configuration.RemoveProjectAsync(101, Token);
        clock.Now = Noon.AddDays(1);

        Assert.Null(await installation.Configuration.RemoveProjectAsync(101, Token));
        Assert.Null(await installation.Configuration.RemoveProjectAsync(999, Token));
        Assert.Equal(Noon, (await installation.Reads.ListProjectsAsync(includeRemoved: true, Token)).Single().RemovedAt);
    }

    [Fact]
    public async Task A_removed_Project_registered_again_is_active_with_its_first_registration_time_and_one_row()
    {
        var clock = new MovingTime(Noon);
        using var installation = await StartAsync(clock);
        await installation.Configuration.RegisterProjectAsync(101, "acme/shop", "main", Token);
        await installation.Configuration.RemoveProjectAsync(101, Token);
        clock.Now = Noon.AddDays(9);

        var again = await installation.Configuration.RegisterProjectAsync(101, "acme/store", "develop", Token);

        Assert.Equal(ProjectChange.Registered, again.Change);
        Assert.Equal(new Project(101, "acme/store", "develop", Noon, null), again.Project);
        Assert.Single(await installation.Reads.ListProjectsAsync(includeRemoved: true, Token));
    }

    [Fact]
    public async Task The_list_is_in_name_order_ignoring_case_then_by_id()
    {
        using var installation = await StartAsync();

        await installation.Configuration.RegisterProjectAsync(5, "acme/web", "main", Token);
        await installation.Configuration.RegisterProjectAsync(4, "Acme/Shop", "main", Token);
        await installation.Configuration.RegisterProjectAsync(3, "acme/api", "main", Token);

        Assert.Equal([3L, 4L, 5L], (await installation.Reads.ListProjectsAsync(includeRemoved: false, Token)).Select(p => p.RepositoryId));
    }

    [Fact]
    public async Task Two_Projects_may_hold_one_name_because_a_stored_name_can_be_stale()
    {
        using var installation = await StartAsync();

        await installation.Configuration.RegisterProjectAsync(1, "acme/shop", "main", Token);
        await installation.Configuration.RegisterProjectAsync(2, "acme/shop", "main", Token);

        Assert.Equal([1L, 2L], (await installation.Reads.ListProjectsAsync(includeRemoved: false, Token)).Select(p => p.RepositoryId));
    }

    [Fact]
    public async Task A_Base_Branch_with_slashes_and_dots_is_kept_as_written()
    {
        using var installation = await StartAsync();

        await installation.Configuration.RegisterProjectAsync(101, "acme/shop", "release/1.0-rc.1", Token);

        Assert.Equal("release/1.0-rc.1", Assert.Single(await installation.Reads.ListProjectsAsync(includeRemoved: false, Token)).BaseBranch);
    }

    [Fact]
    public async Task Concurrent_registrations_of_one_repository_store_one_row_and_one_writer_registers_it()
    {
        using var installation = await StartAsync();

        var writes = await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
            installation.Configuration.RegisterProjectAsync(101, "acme/shop", $"branch-{i}", Token)));

        Assert.Single(writes, w => w.Change == ProjectChange.Registered);
        Assert.Single(await installation.Reads.ListProjectsAsync(includeRemoved: true, Token));
    }

    [Fact]
    public async Task Concurrent_removals_of_one_Project_remove_it_once_and_never_fail()
    {
        using var installation = await StartAsync();
        await installation.Configuration.RegisterProjectAsync(101, "acme/shop", "main", Token);

        var removals = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => installation.Configuration.RemoveProjectAsync(101, Token)));

        Assert.Single(removals, removed => removed is not null);
    }

    [Fact]
    public async Task Removals_racing_registrations_never_fail()
    {
        using var installation = await StartAsync();

        await Task.WhenAll(Enumerable.Range(0, 20).Select(async i =>
        {
            await installation.Configuration.RegisterProjectAsync(100 + (i % 4), "acme/shop", "main", Token);
            await installation.Configuration.RemoveProjectAsync(100 + (i % 4), Token);
        }));
    }

    [Fact]
    public async Task The_list_and_the_removed_Projects_are_unchanged_after_a_restart()
    {
        var clock = new MovingTime(Noon);
        using var installation = await StartAsync(clock);
        await installation.Configuration.RegisterProjectAsync(101, "acme/shop", "main", Token);
        await installation.Configuration.RegisterProjectAsync(102, "acme/api", "develop", Token);
        await installation.Configuration.RemoveProjectAsync(102, Token);

        await using var restarted = installation.Restart();
        await restarted.GetRequiredService<DatabaseMigrator>().MigrateAsync(Token);
        var reads = restarted.GetRequiredService<IReadStore>();

        Assert.Equal(await installation.Reads.ListProjectsAsync(includeRemoved: true, Token), await reads.ListProjectsAsync(includeRemoved: true, Token));
        Assert.Equal([101L], (await reads.ListProjectsAsync(includeRemoved: false, Token)).Select(p => p.RepositoryId));
    }

    [Fact]
    public async Task The_read_only_connection_cannot_register_a_Project()
    {
        using var installation = await StartAsync();
        await using var context = installation.Database.OpenReader();

        await Assert.ThrowsAnyAsync<Exception>(() => context.Database.ExecuteSqlRawAsync(
            """INSERT INTO "Projects" ("RepositoryId", "FullName", "BaseBranch", "RegisteredAt") VALUES (1, 'a/b', 'main', '2026-11-27T12:00:00.0000000Z')""", Token));
    }
}
