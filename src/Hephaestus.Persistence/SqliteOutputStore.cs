using Hephaestus.Core.Output;
using Hephaestus.Core.Ports;
using Hephaestus.Persistence.Model;
using Microsoft.EntityFrameworkCore;

namespace Hephaestus.Persistence;

/// <summary>
/// <see cref="IOutputStore"/> over files and the <c>OutputFiles</c> table (AD-18, FR-53): one file for each container run under
/// <c>output/&lt;task&gt;/&lt;step-run&gt;.log</c> in the data directory, and one row that indexes it. The file holds what the runtime wrote, which it masked first.
/// </summary>
internal sealed class SqliteOutputStore(HephaestusDatabase database, TimeProvider time) : IOutputStore
{
    /// <summary>The directory of the output files, under the data directory.</summary>
    public const string DirectoryName = "output";

    private string Root => Path.Combine(database.Options.DataDirectory, DirectoryName);

    public async Task<IOutputWriter> BeginAsync(OutputRun run, CancellationToken cancellationToken)
    {
        var relative = $"{run.TaskId:D}/{run.StepRunId:D}.log";
        var file = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);

        // The file is created first, and replaces one of the step run that is already there: a restart reads a container's whole log again. A row is never left that
        // names a file that could not be made.
        var stream = new FileStream(file, new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.Read, Options = FileOptions.Asynchronous });
        var startedAt = time.GetUtcNow();
        try
        {
            await using var context = database.OpenWriter();
            var taskId = EntityIdConverter.ToText(run.TaskId);
            var stepRunId = EntityIdConverter.ToText(run.StepRunId);
            var started = UtcInstantConverter.ToText(startedAt);
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO "OutputFiles" ("StepRunId", "TaskId", "ContainerId", "Path", "Bytes", "StartedAt", "CompletedAt")
                VALUES ({stepRunId}, {taskId}, {run.ContainerId}, {relative}, 0, {started}, NULL)
                ON CONFLICT ("StepRunId") DO UPDATE SET "TaskId" = excluded."TaskId", "ContainerId" = excluded."ContainerId", "Path" = excluded."Path", "Bytes" = 0, "StartedAt" = excluded."StartedAt", "CompletedAt" = NULL
                """,
                cancellationToken);
        }
        catch
        {
            await stream.DisposeAsync();
            throw;
        }

        return new Writer(database, time, run, startedAt, stream);
    }

    public async Task<StoredOutput?> FindAsync(Guid stepRunId, CancellationToken cancellationToken)
    {
        await using var context = database.OpenReader();
        var entry = await context.OutputFiles.SingleOrDefaultAsync(o => o.StepRunId == stepRunId, cancellationToken);
        return entry is null ? null : ToStored(entry);
    }

    public async Task<Stream?> OpenReadAsync(Guid stepRunId, CancellationToken cancellationToken)
    {
        await using var context = database.OpenReader();
        var entry = await context.OutputFiles.SingleOrDefaultAsync(o => o.StepRunId == stepRunId, cancellationToken);
        if (entry is null)
        {
            return null;
        }

        try
        {
            return new FileStream(Path.Combine(Root, entry.Path), new FileStreamOptions { Mode = FileMode.Open, Access = FileAccess.Read, Share = FileShare.ReadWrite, Options = FileOptions.Asynchronous | FileOptions.SequentialScan });
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    private static StoredOutput ToStored(OutputFileEntry entry) => new(entry.TaskId, entry.StepRunId, entry.ContainerId, entry.Bytes, entry.StartedAt, entry.CompletedAt);

    private sealed class Writer(HephaestusDatabase database, TimeProvider time, OutputRun run, DateTimeOffset startedAt, FileStream stream) : IOutputWriter
    {
        private long _bytes;
        private bool _closed;
        private StoredOutput? _completed;

        public async ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            await stream.WriteAsync(bytes, cancellationToken);
            // What was written is in the file when this returns, so that a step that is stopped keeps its output.
            await stream.FlushAsync(cancellationToken);
            _bytes += bytes.Length;
        }

        public async Task<StoredOutput> CompleteAsync(CancellationToken cancellationToken)
        {
            if (_completed is not null)
            {
                return _completed;
            }

            ObjectDisposedException.ThrowIf(_closed, this);
            // Recorded before the file is closed, so that a completion that fails, because the database does, can be made again.
            await stream.FlushAsync(cancellationToken);
            var completedAt = time.GetUtcNow();
            await RecordAsync(completedAt, cancellationToken);
            await Close();
            return _completed = new StoredOutput(run.TaskId, run.StepRunId, run.ContainerId, _bytes, startedAt, completedAt);
        }

        public async ValueTask DisposeAsync()
        {
            if (_closed)
            {
                return;
            }

            // Not completed (a writer that was is closed): the file stays as it is, and the index says how much of it there is. A failure to say so leaves the size the
            // index had; the file is the record, and the index is read against it.
            await Close();
            try
            {
                await RecordAsync(completedAt: null, CancellationToken.None);
            }
            catch (Exception exception) when (exception is DbUpdateException or Microsoft.Data.Sqlite.SqliteException)
            {
                // See above.
            }
        }

        private async Task Close()
        {
            _closed = true;
            await stream.DisposeAsync();
        }

        private async Task RecordAsync(DateTimeOffset? completedAt, CancellationToken cancellationToken)
        {
            await using var context = database.OpenWriter();
            var stepRunId = EntityIdConverter.ToText(run.StepRunId);
            var completed = completedAt is { } instant ? UtcInstantConverter.ToText(instant) : null;
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "OutputFiles" SET "Bytes" = {_bytes}, "CompletedAt" = {completed} WHERE "StepRunId" = {stepRunId}""",
                cancellationToken);
        }
    }
}
