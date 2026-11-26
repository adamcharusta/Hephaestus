using System.Text;
using Hephaestus.Core.Identity;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Secrets;
using Hephaestus.Persistence.Model;
using Microsoft.EntityFrameworkCore;

namespace Hephaestus.Persistence;

/// <summary>
/// <see cref="ISecretStore"/>: a value is a file named after the secret on the secrets volume, written with mode 0600 through a
/// temporary file and a rename; the database records the name (AD-16). A write puts the file down first and the name second, and a
/// removal takes the file first and the name second, so a failure in between leaves a name that a repeat of the command settles,
/// never a value without a way to remove it.
/// </summary>
internal sealed class FileSecretStore(HephaestusDatabase database, TimeProvider time) : ISecretStore
{
    private const UnixFileMode OwnerOnlyFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode OwnerOnlyDirectory = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private const string TemporaryFileSuffix = ".tmp";
    private const string TemporaryFilePattern = "." + "*" + TemporaryFileSuffix;

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    // One host process writes the database (AD-17), so one gate keeps two requests for the same name apart.
    private readonly SemaphoreSlim _gate = new(1, 1);

    public event Func<Task>? Changed;

    private string SecretsDirectory => database.Options.SecretsDirectory;

    public async Task SetAsync(string name, string value, CancellationToken cancellationToken)
    {
        RequireName(name);
        var refusal = SecretPolicy.CheckValue(value);
        if (refusal != SecretValueRefusal.None)
        {
            // The message names the rule that failed and never the value.
            throw new ArgumentException($"The secret value is refused: {refusal}.", nameof(value));
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureDirectory();
            RemoveAbandonedTemporaryFiles();
            await WriteFileAsync(PathOf(name), value, cancellationToken);

            try
            {
                await using var context = database.OpenWriter();
                if (!await context.SecretNames.AnyAsync(s => s.Name == name, cancellationToken))
                {
                    context.SecretNames.Add(new SecretName { Id = EntityId.New(time), Name = name, CreatedAt = time.GetUtcNow() });
                    await context.SaveChangesAsync(cancellationToken);
                }
            }
            finally
            {
                // The file is on the volume whatever became of the name, so whoever masks must know the value.
                await NotifyChangedAsync();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> RemoveAsync(string name, CancellationToken cancellationToken)
    {
        RequireName(name);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var path = PathOf(name);
            var hadFile = File.Exists(path);
            if (hadFile)
            {
                File.Delete(path);
            }

            await using var context = database.OpenWriter();
            var hadName = await context.SecretNames.Where(s => s.Name == name).ExecuteDeleteAsync(cancellationToken) > 0;
            if (!hadFile && !hadName)
            {
                return false;
            }

            await NotifyChangedAsync();
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string?> ReadAsync(string name, CancellationToken cancellationToken)
    {
        if (!SecretPolicy.IsValidName(name))
        {
            return null;
        }

        try
        {
            return await File.ReadAllTextAsync(PathOf(name), Utf8, cancellationToken);
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

    public async Task<IReadOnlyList<string>> ReadAllAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(SecretsDirectory))
        {
            return [];
        }

        var values = new List<string>();
        foreach (var path in EnumerateSecretFiles())
        {
            // A temporary file and any other file that is not a secret's are not masked: no valid name looks like them.
            if (!SecretPolicy.IsValidName(Path.GetFileName(path)))
            {
                continue;
            }

            try
            {
                values.Add(await File.ReadAllTextAsync(path, Utf8, cancellationToken));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Removed since the directory was listed, or a file this process cannot read: nothing in the host can hold its
                // value either, because reading it is what ReadAsync does too.
            }
        }

        return values;
    }

    private IEnumerable<string> EnumerateSecretFiles()
    {
        try
        {
            return Directory.EnumerateFiles(SecretsDirectory).ToList();
        }
        catch (DirectoryNotFoundException)
        {
            return [];
        }
    }

    /// <summary>Tells whoever depends on the stored values, in turn, and waits for each; the gate is held, so changes arrive in order.</summary>
    private async Task NotifyChangedAsync()
    {
        var handlers = Changed;
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<Func<Task>>())
        {
            await handler();
        }
    }

    private static void RequireName(string name)
    {
        if (!SecretPolicy.IsValidName(name))
        {
            throw new ArgumentException("The secret name is not valid.", nameof(name));
        }
    }

    private string PathOf(string name) => Path.Combine(SecretsDirectory, name);

    /// <summary>The secrets directory exists and only its owner can enter it, whoever made it and with what mode.</summary>
    private void EnsureDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(SecretsDirectory);
            return;
        }

        Directory.CreateDirectory(SecretsDirectory, OwnerOnlyDirectory);
        File.SetUnixFileMode(SecretsDirectory, OwnerOnlyDirectory);
    }

    /// <summary>
    /// A temporary file outlives a host that died between writing it and renaming it. No name is ever that of a temporary file, so
    /// neither <c>list</c> nor <c>remove</c> would reach one; the next write sweeps them, under the gate, when none is in use.
    /// </summary>
    private void RemoveAbandonedTemporaryFiles()
    {
        foreach (var abandoned in Directory.EnumerateFiles(SecretsDirectory, TemporaryFilePattern))
        {
            File.Delete(abandoned);
        }
    }

    private async Task WriteFileAsync(string path, string value, CancellationToken cancellationToken)
    {
        // A valid name never starts with a dot, so no secret can have the name of a temporary file.
        var temporary = Path.Combine(SecretsDirectory, $".{Guid.NewGuid():N}{TemporaryFileSuffix}");
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = OwnerOnlyFile;
        }

        try
        {
            await using (var stream = new FileStream(temporary, options))
            {
                await stream.WriteAsync(Utf8.GetBytes(value), cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            File.Delete(temporary);
            throw;
        }
    }
}
