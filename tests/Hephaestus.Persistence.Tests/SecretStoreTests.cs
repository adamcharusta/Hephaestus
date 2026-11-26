using System.Text;
using Hephaestus.Core.Secrets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hephaestus.Persistence.Tests;

/// <summary>Story 1.5, AD-16: a value is a file on the secrets volume, the database holds the name, and nothing of the value is in the database.</summary>
public class SecretStoreTests
{
    private const string Value = "sk-test-0123456789-abcdefghij";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_value_is_a_file_named_after_the_secret_and_the_database_holds_the_name()
    {
        using var installation = new TempInstallation();
        await installation.MigrateAsync();

        await installation.Secrets.SetAsync("anthropic-api-key", Value, Token);

        Assert.Equal(Value, await File.ReadAllTextAsync(Path.Combine(installation.Options.SecretsDirectory, "anthropic-api-key"), Token));
        Assert.Equal(["anthropic-api-key"], await installation.Reads.ListSecretNamesAsync(Token));
        Assert.Equal(Value, await installation.Secrets.ReadAsync("anthropic-api-key", Token));
    }

    [Fact]
    public async Task The_value_is_in_no_file_of_the_database()
    {
        using var installation = new TempInstallation();
        await installation.MigrateAsync();
        await installation.Secrets.SetAsync("anthropic-api-key", Value, Token);
        await installation.Secrets.SetAsync("other", Value + "-2", Token);
        await installation.Secrets.RemoveAsync("other", Token);

        var files = installation.FilesOfTheDatabase().ToList();

        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new MemoryStream();
            await stream.CopyToAsync(reader, Token);
            var text = Encoding.Latin1.GetString(reader.ToArray());
            Assert.DoesNotContain(Value, text, StringComparison.Ordinal);
            Assert.DoesNotContain(Value.Split('-')[2], text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task The_row_holds_a_uuid_version_7_id_the_name_and_a_fixed_width_utc_instant()
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 11, 26, 21, 30, 5, TimeSpan.Zero));
        using var installation = new TempInstallation(clock);
        await installation.MigrateAsync();
        await installation.Secrets.SetAsync("anthropic-api-key", Value, Token);

        await using var context = installation.Database.OpenReader();
        await context.Database.OpenConnectionAsync(Token);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT Id, Name, CreatedAt FROM SecretNames";
        await using var rows = await command.ExecuteReaderAsync(Token);

        Assert.True(await rows.ReadAsync(Token));
        Assert.Matches("^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$", rows.GetString(0));
        Assert.Equal("anthropic-api-key", rows.GetString(1));
        Assert.Equal("2026-11-26T21:30:05.0000000Z", rows.GetString(2));
        Assert.Equal(3, rows.FieldCount);
        Assert.False(await rows.ReadAsync(Token));
    }

    [Fact]
    public async Task Setting_a_name_again_replaces_the_value_and_keeps_one_name()
    {
        using var installation = new TempInstallation();
        await installation.MigrateAsync();

        await installation.Secrets.SetAsync("token", Value, Token);
        await installation.Secrets.SetAsync("token", Value + "-new", Token);

        Assert.Equal(Value + "-new", await installation.Secrets.ReadAsync("token", Token));
        Assert.Equal(["token"], await installation.Reads.ListSecretNamesAsync(Token));
        Assert.Equal(["token"], Directory.EnumerateFiles(installation.Options.SecretsDirectory).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Names_are_listed_in_order_and_nothing_else_is_listed()
    {
        using var installation = new TempInstallation();
        await installation.MigrateAsync();

        await installation.Secrets.SetAsync("b-secret", Value, Token);
        await installation.Secrets.SetAsync("a-secret", Value, Token);

        Assert.Equal(["a-secret", "b-secret"], await installation.Reads.ListSecretNamesAsync(Token));
    }

    [Fact]
    public async Task Removing_a_secret_removes_its_file_and_its_name()
    {
        using var installation = new TempInstallation();
        await installation.MigrateAsync();
        await installation.Secrets.SetAsync("token", Value, Token);

        var removed = await installation.Secrets.RemoveAsync("token", Token);

        Assert.True(removed);
        Assert.Empty(await installation.Reads.ListSecretNamesAsync(Token));
        Assert.Empty(Directory.EnumerateFiles(installation.Options.SecretsDirectory));
        Assert.Null(await installation.Secrets.ReadAsync("token", Token));
    }

    [Fact]
    public async Task Removing_a_secret_that_is_not_stored_says_so()
    {
        using var installation = new TempInstallation();
        await installation.MigrateAsync();

        Assert.False(await installation.Secrets.RemoveAsync("never-stored", Token));
    }

    [Fact]
    public async Task A_name_whose_file_is_gone_is_still_removed()
    {
        using var installation = new TempInstallation();
        await installation.MigrateAsync();
        await installation.Secrets.SetAsync("token", Value, Token);
        File.Delete(Path.Combine(installation.Options.SecretsDirectory, "token"));

        Assert.True(await installation.Secrets.RemoveAsync("token", Token));
        Assert.Empty(await installation.Reads.ListSecretNamesAsync(Token));
    }

    [Fact]
    public async Task A_file_without_a_name_is_still_removed()
    {
        using var installation = new TempInstallation();
        await installation.MigrateAsync();
        await installation.Secrets.SetAsync("token", Value, Token);
        await using (var context = installation.Database.OpenWriter())
        {
            await context.SecretNames.ExecuteDeleteAsync(Token);
        }

        Assert.True(await installation.Secrets.RemoveAsync("token", Token));
        Assert.Empty(Directory.EnumerateFiles(installation.Options.SecretsDirectory));
    }

    [Fact]
    public async Task The_secrets_are_still_there_when_the_host_starts_again()
    {
        using var installation = new TempInstallation();
        await installation.MigrateAsync();
        await installation.Secrets.SetAsync("token", Value, Token);

        await using var restarted = installation.Restart();
        await restarted.GetRequiredService<DatabaseMigrator>().MigrateAsync(Token);

        Assert.Equal(["token"], await restarted.GetRequiredService<Hephaestus.Core.Ports.IReadStore>().ListSecretNamesAsync(Token));
        Assert.Equal(Value, await restarted.GetRequiredService<Hephaestus.Core.Ports.ISecretStore>().ReadAsync("token", Token));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../escape")]
    [InlineData("a/b")]
    [InlineData(".hidden")]
    [InlineData("UPPER")]
    [InlineData("with space")]
    [InlineData("-leading-hyphen")]
    public async Task A_name_outside_the_rule_is_refused_and_nothing_is_written(string name)
    {
        using var installation = new TempInstallation();
        await installation.MigrateAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => installation.Secrets.SetAsync(name, Value, Token));
        await Assert.ThrowsAsync<ArgumentException>(() => installation.Secrets.RemoveAsync(name, Token));
        Assert.Null(await installation.Secrets.ReadAsync(name, Token));
        Assert.False(Directory.Exists(installation.Options.SecretsDirectory));
    }

    [Theory]
    [InlineData("")]
    [InlineData("        ")]
    [InlineData("short")]
    public async Task A_refused_value_is_not_stored_and_the_refusal_does_not_repeat_it(string value)
    {
        using var installation = new TempInstallation();
        await installation.MigrateAsync();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => installation.Secrets.SetAsync("token", value, Token));

        Assert.StartsWith("The secret value is refused: ", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("short", exception.Message, StringComparison.Ordinal);
        Assert.Empty(await installation.Reads.ListSecretNamesAsync(Token));
    }

    [Fact]
    public async Task The_longest_value_is_stored_and_one_character_more_is_refused()
    {
        using var installation = new TempInstallation();
        await installation.MigrateAsync();

        await installation.Secrets.SetAsync("long", new string('x', SecretPolicy.MaximumValueLength), Token);
        await Assert.ThrowsAsync<ArgumentException>(() => installation.Secrets.SetAsync("longer", new string('x', SecretPolicy.MaximumValueLength + 1), Token));

        Assert.Equal(["long"], await installation.Reads.ListSecretNamesAsync(Token));
    }

    [Fact]
    public async Task A_value_with_line_breaks_and_non_ascii_text_is_kept_exactly()
    {
        using var installation = new TempInstallation();
        await installation.MigrateAsync();
        const string Key = "-----BEGIN KEY-----\nzażółć gęślą jaźń\r\n-----END KEY-----\n";

        await installation.Secrets.SetAsync("key", Key, Token);

        Assert.Equal(Key, await installation.Secrets.ReadAsync("key", Token));
        Assert.Equal(Encoding.UTF8.GetBytes(Key), await File.ReadAllBytesAsync(Path.Combine(installation.Options.SecretsDirectory, "key"), Token));
    }

    [Fact]
    public async Task A_missing_secret_reads_as_nothing_before_any_secret_was_ever_stored()
    {
        using var installation = new TempInstallation();

        Assert.Null(await installation.Secrets.ReadAsync("token", Token));
    }

    [Fact]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public async Task The_directory_and_the_file_can_be_entered_and_read_by_their_owner_only_and_no_temporary_file_is_left()
    {
        using var installation = new TempInstallation();
        await installation.MigrateAsync();

        await installation.Secrets.SetAsync("token", Value, Token);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(installation.Options.SecretsDirectory));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(installation.Options.SecretsDirectory, "token")));
        Assert.Equal(["token"], Directory.EnumerateFileSystemEntries(installation.Options.SecretsDirectory).Select(Path.GetFileName));
    }

    [Fact]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public async Task A_directory_that_others_could_enter_is_closed_to_them_before_a_value_is_written()
    {
        using var installation = new TempInstallation();
        await installation.MigrateAsync();
        Directory.CreateDirectory(installation.Options.SecretsDirectory);
        File.SetUnixFileMode(installation.Options.SecretsDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        await installation.Secrets.SetAsync("token", Value, Token);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(installation.Options.SecretsDirectory));
    }

    [Fact]
    public async Task A_temporary_file_a_dead_host_left_behind_is_swept_by_the_next_write()
    {
        using var installation = new TempInstallation();
        await installation.MigrateAsync();
        Directory.CreateDirectory(installation.Options.SecretsDirectory);
        var abandoned = Path.Combine(installation.Options.SecretsDirectory, ".0123456789abcdef.tmp");
        await File.WriteAllTextAsync(abandoned, Value, Token);

        await installation.Secrets.SetAsync("token", Value, Token);

        Assert.False(File.Exists(abandoned));
        Assert.Equal(["token"], Directory.EnumerateFileSystemEntries(installation.Options.SecretsDirectory).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Concurrent_requests_for_one_name_leave_one_name_and_one_file()
    {
        using var installation = new TempInstallation();
        await installation.MigrateAsync();

        await Task.WhenAll(Enumerable.Range(0, 12).Select(i => installation.Secrets.SetAsync("token", $"{Value}-{i}", Token)));

        Assert.Equal(["token"], await installation.Reads.ListSecretNamesAsync(Token));
        Assert.Single(Directory.EnumerateFiles(installation.Options.SecretsDirectory));
    }

    [Fact]
    public async Task A_failed_write_leaves_no_temporary_file()
    {
        using var installation = new TempInstallation();
        await installation.MigrateAsync();
        Directory.CreateDirectory(Path.Combine(installation.Options.SecretsDirectory, "token"));

        await Assert.ThrowsAnyAsync<IOException>(() => installation.Secrets.SetAsync("token", Value, Token));

        Assert.Equal(["token"], Directory.EnumerateFileSystemEntries(installation.Options.SecretsDirectory).Select(Path.GetFileName));
    }
}
