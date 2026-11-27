using System.Text;
using Hephaestus.Core.Output;
using Hephaestus.Core.Ports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hephaestus.Persistence.Tests;

/// <summary>Story 3.3, AD-18, FR-53: the output of a container run is one file, indexed in the database, that is complete when the run ended and kept as far as it got when it did not.</summary>
public class OutputStoreTests
{
    private static readonly Guid TaskId = Guid.Parse("018f4b6e-0000-7000-8000-000000000001");
    private static readonly Guid StepRunId = Guid.Parse("018f4b6e-0000-7000-8000-000000000002");
    private static readonly DateTimeOffset Noon = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static OutputRun Run(Guid? stepRun = null, string container = "container-1") => new(TaskId, stepRun ?? StepRunId, container);

    private static async Task<TempInstallation> StartAsync()
    {
        var installation = new TempInstallation(new FixedTimeProvider(Noon));
        await installation.MigrateAsync();
        return installation;
    }

    private static async Task<string> ReadAllAsync(IOutputStore store, Guid stepRun)
    {
        await using var stream = await store.OpenReadAsync(stepRun, Token) ?? throw new InvalidOperationException("No output.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync(Token);
    }

    [Fact]
    public async Task A_completed_run_is_one_file_under_the_data_directory_and_one_row_that_says_how_big_it_is()
    {
        using var installation = await StartAsync();
        var store = installation.Provider.GetRequiredService<IOutputStore>();

        await using (var writer = await store.BeginAsync(Run(), Token))
        {
            await writer.WriteAsync(Encoding.UTF8.GetBytes("first line\n"), Token);
            await writer.WriteAsync(Encoding.UTF8.GetBytes("second line\n"), Token);
            var completed = await writer.CompleteAsync(Token);
            Assert.Equal(23, completed.Bytes);
        }

        var stored = await store.FindAsync(StepRunId, Token);
        Assert.Equal(new StoredOutput(TaskId, StepRunId, "container-1", 23, Noon, Noon), stored);
        Assert.True(stored!.Complete);
        Assert.Equal("first line\nsecond line\n", await ReadAllAsync(store, StepRunId));
        var file = Assert.Single(Directory.EnumerateFiles(Path.Combine(installation.Options.DataDirectory, "output"), "*", SearchOption.AllDirectories));
        Assert.Equal(Path.Combine(installation.Options.DataDirectory, "output", TaskId.ToString("D"), $"{StepRunId:D}.log"), file);
    }

    [Fact]
    public async Task What_was_written_is_in_the_file_before_the_run_is_completed()
    {
        using var installation = await StartAsync();
        var store = installation.Provider.GetRequiredService<IOutputStore>();

        await using var writer = await store.BeginAsync(Run(), Token);
        await writer.WriteAsync(Encoding.UTF8.GetBytes("so far"), Token);

        Assert.Equal("so far", await ReadAllAsync(store, StepRunId));
        var started = await store.FindAsync(StepRunId, Token);
        Assert.False(started!.Complete);
        Assert.Equal(0, started.Bytes);
    }

    [Fact]
    public async Task A_writer_disposed_before_completion_keeps_the_output_and_is_indexed_as_not_complete()
    {
        using var installation = await StartAsync();
        var store = installation.Provider.GetRequiredService<IOutputStore>();

        await using (var writer = await store.BeginAsync(Run(), Token))
        {
            await writer.WriteAsync(Encoding.UTF8.GetBytes("written before the host stopped"), Token);
        }

        var stored = await store.FindAsync(StepRunId, Token);
        Assert.False(stored!.Complete);
        Assert.Equal(31, stored.Bytes);
        Assert.Null(stored.CompletedAt);
        Assert.Equal("written before the host stopped", await ReadAllAsync(store, StepRunId));
    }

    [Fact]
    public async Task A_writer_that_is_completed_and_disposed_leaves_the_complete_entry_and_cannot_be_written_to_again()
    {
        using var installation = await StartAsync();
        var store = installation.Provider.GetRequiredService<IOutputStore>();
        var writer = await store.BeginAsync(Run(), Token);
        await writer.WriteAsync(Encoding.UTF8.GetBytes("done"), Token);
        var first = await writer.CompleteAsync(Token);

        Assert.Equal(first, await writer.CompleteAsync(Token));
        await writer.DisposeAsync();
        await writer.DisposeAsync();

        Assert.True((await store.FindAsync(StepRunId, Token))!.Complete);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await writer.WriteAsync(new byte[] { 1 }, Token));
    }

    [Fact]
    public async Task Beginning_again_for_the_same_step_run_replaces_the_file_and_the_row()
    {
        using var installation = await StartAsync();
        var store = installation.Provider.GetRequiredService<IOutputStore>();
        await using (var first = await store.BeginAsync(Run(container: "container-1"), Token))
        {
            await first.WriteAsync(Encoding.UTF8.GetBytes("a long first attempt"), Token);
            await first.CompleteAsync(Token);
        }

        await using (var second = await store.BeginAsync(Run(container: "container-2"), Token))
        {
            await second.WriteAsync(Encoding.UTF8.GetBytes("again"), Token);
            await second.CompleteAsync(Token);
        }

        Assert.Equal("again", await ReadAllAsync(store, StepRunId));
        var stored = await store.FindAsync(StepRunId, Token);
        Assert.Equal(("container-2", 5L), (stored!.ContainerId, stored.Bytes));
    }

    [Fact]
    public async Task Each_step_run_has_a_file_of_its_own()
    {
        using var installation = await StartAsync();
        var store = installation.Provider.GetRequiredService<IOutputStore>();
        var other = Guid.Parse("018f4b6e-0000-7000-8000-000000000003");

        await using (var one = await store.BeginAsync(Run(), Token))
        {
            await one.WriteAsync(Encoding.UTF8.GetBytes("one"), Token);
            await one.CompleteAsync(Token);
        }

        await using (var two = await store.BeginAsync(Run(other, "container-2"), Token))
        {
            await two.WriteAsync(Encoding.UTF8.GetBytes("two"), Token);
            await two.CompleteAsync(Token);
        }

        Assert.Equal("one", await ReadAllAsync(store, StepRunId));
        Assert.Equal("two", await ReadAllAsync(store, other));
    }

    [Fact]
    public async Task A_step_run_with_no_output_has_no_entry_and_nothing_to_read()
    {
        using var installation = await StartAsync();
        var store = installation.Provider.GetRequiredService<IOutputStore>();

        Assert.Null(await store.FindAsync(StepRunId, Token));
        Assert.Null(await store.OpenReadAsync(StepRunId, Token));
    }

    [Fact]
    public async Task An_entry_whose_file_is_gone_has_nothing_to_read()
    {
        using var installation = await StartAsync();
        var store = installation.Provider.GetRequiredService<IOutputStore>();
        await using (var writer = await store.BeginAsync(Run(), Token))
        {
            await writer.CompleteAsync(Token);
        }

        File.Delete(Path.Combine(installation.Options.DataDirectory, "output", TaskId.ToString("D"), $"{StepRunId:D}.log"));
        Assert.Null(await store.OpenReadAsync(StepRunId, Token));

        Directory.Delete(Path.Combine(installation.Options.DataDirectory, "output"), recursive: true);
        Assert.Null(await store.OpenReadAsync(StepRunId, Token));
    }

    [Fact]
    public async Task The_output_outlives_a_restart_of_the_host()
    {
        using var installation = await StartAsync();
        await using (var writer = await installation.Provider.GetRequiredService<IOutputStore>().BeginAsync(Run(), Token))
        {
            await writer.WriteAsync(Encoding.UTF8.GetBytes("kept"), Token);
            await writer.CompleteAsync(Token);
        }

        using var restarted = installation.Restart();
        var store = restarted.GetRequiredService<IOutputStore>();

        Assert.True((await store.FindAsync(StepRunId, Token))!.Complete);
        Assert.Equal("kept", await ReadAllAsync(store, StepRunId));
    }

    [Fact]
    public async Task A_file_that_cannot_be_created_leaves_no_row_that_names_it()
    {
        using var installation = await StartAsync();
        var store = installation.Provider.GetRequiredService<IOutputStore>();
        // A directory where the file belongs.
        Directory.CreateDirectory(Path.Combine(installation.Options.DataDirectory, "output", TaskId.ToString("D"), $"{StepRunId:D}.log"));

        await Assert.ThrowsAnyAsync<Exception>(() => store.BeginAsync(Run(), Token));

        Assert.Null(await store.FindAsync(StepRunId, Token));
    }

    [Fact]
    public async Task A_completion_that_fails_because_the_database_does_can_be_made_again_and_then_the_file_is_complete()
    {
        using var installation = await StartAsync();
        var store = installation.Provider.GetRequiredService<IOutputStore>();
        await using var writer = await store.BeginAsync(Run(), Token);
        await writer.WriteAsync(Encoding.UTF8.GetBytes("some output"), Token);
        await using (var context = installation.Database.OpenWriter())
        {
            await context.Database.ExecuteSqlRawAsync("ALTER TABLE \"OutputFiles\" RENAME TO \"OutputFilesAway\"", Token);
        }

        await Assert.ThrowsAnyAsync<Exception>(() => writer.CompleteAsync(Token));
        await using (var context = installation.Database.OpenWriter())
        {
            await context.Database.ExecuteSqlRawAsync("ALTER TABLE \"OutputFilesAway\" RENAME TO \"OutputFiles\"", Token);
        }

        var completed = await writer.CompleteAsync(Token);

        Assert.Equal(11, completed.Bytes);
        Assert.True((await store.FindAsync(StepRunId, Token))!.Complete);
        Assert.Equal("some output", await ReadAllAsync(store, StepRunId));
    }

    [Fact]
    public async Task A_writer_whose_index_cannot_be_updated_when_it_is_disposed_leaves_the_file_and_does_not_throw()
    {
        using var installation = await StartAsync();
        var store = installation.Provider.GetRequiredService<IOutputStore>();
        var writer = await store.BeginAsync(Run(), Token);
        await writer.WriteAsync(Encoding.UTF8.GetBytes("kept anyway"), Token);
        await using (var context = installation.Database.OpenWriter())
        {
            await context.Database.ExecuteSqlRawAsync("ALTER TABLE \"OutputFiles\" RENAME TO \"OutputFilesAway\"", Token);
        }

        await writer.DisposeAsync();

        await using (var context = installation.Database.OpenWriter())
        {
            await context.Database.ExecuteSqlRawAsync("ALTER TABLE \"OutputFilesAway\" RENAME TO \"OutputFiles\"", Token);
        }

        Assert.Equal("kept anyway", await ReadAllAsync(store, StepRunId));
    }
}
