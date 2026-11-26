using System.Net;
using System.Text;
using Hephaestus.Core.Secrets;

namespace Hephaestus.Core.Tests.Secrets;

/// <summary>Story 1.6, AD-16: a stored value is replaced by a mask in plain, base64, and URL-encoded form, across chunk boundaries, and as secrets change.</summary>
public class SecretMaskerTests
{
    private const string Value = "sk-test-0123456789-abcdefghij";
    private const string Mask = SecretMasker.Replacement;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<SecretMasker> BuiltOverAsync(params string[] values)
    {
        var masker = new SecretMasker(new FakeSecretStore(values));
        await masker.RebuildAsync(Token);
        return masker;
    }

    /// <summary>Every form the masker promises to catch, for a value that needs escaping in a URL.</summary>
    public static TheoryData<string> Forms()
    {
        const string Awkward = "p@ss word/with+symbols=1";
        return new TheoryData<string>
        {
            Value,
            Convert.ToBase64String(Encoding.UTF8.GetBytes(Value)),
            Convert.ToBase64String(Encoding.UTF8.GetBytes(Value)).TrimEnd('='),
            Uri.EscapeDataString(Awkward),
            WebUtility.UrlEncode(Awkward),
            Awkward,
            Convert.ToBase64String(Encoding.UTF8.GetBytes(Awkward)),
        };
    }

    [Theory]
    [MemberData(nameof(Forms))]
    public async Task A_value_in_plain_base64_or_url_encoded_form_is_replaced_by_a_mask(string form)
    {
        var masker = await BuiltOverAsync(Value, "p@ss word/with+symbols=1");

        Assert.Equal($"token={Mask}; next", masker.Mask($"token={form}; next"));
        Assert.Equal(Mask, masker.Mask(form));
    }

    [Fact]
    public async Task Text_without_a_secret_is_unchanged_and_so_is_everything_when_no_secret_is_stored()
    {
        var masker = await BuiltOverAsync(Value);
        var empty = await BuiltOverAsync();

        Assert.Equal("nothing to hide: sk-test, 0123456789, abcdefghij", masker.Mask("nothing to hide: sk-test, 0123456789, abcdefghij"));
        Assert.Equal(string.Empty, masker.Mask(string.Empty));
        Assert.Equal(Value, empty.Mask(Value));
    }

    [Fact]
    public async Task Every_occurrence_is_replaced()
    {
        var masker = await BuiltOverAsync(Value);

        Assert.Equal($"{Mask} and {Mask}{Mask}", masker.Mask($"{Value} and {Value}{Value}"));
    }

    [Fact]
    public async Task A_stored_value_shorter_than_the_minimum_masks_nothing()
    {
        var masker = await BuiltOverAsync("short", "");

        Assert.Equal("short and short", masker.Mask("short and short"));
    }

    [Fact]
    public async Task A_value_in_a_file_that_ends_in_a_line_break_is_masked_without_it()
    {
        var masker = await BuiltOverAsync(Value + "\n", "another-value-0987654321\r\n");

        Assert.Equal($"a {Mask} b", masker.Mask($"a {Value} b"));
        Assert.Equal($"a {Mask} b", masker.Mask($"a another-value-0987654321 b"));
    }

    [Fact]
    public async Task A_stored_value_that_is_only_white_space_masks_nothing()
    {
        var masker = await BuiltOverAsync("          ", "\n\n\n\n\n\n\n\n\n");

        Assert.Equal("a  b   c\n\nd", masker.Mask("a  b   c\n\nd"));
    }

    [Fact]
    public async Task Values_that_overlap_are_masked_as_one_run_so_no_tail_of_either_is_left()
    {
        var masker = await BuiltOverAsync("abcdefgh", "defghijk");

        Assert.Equal(Mask, masker.Mask("abcdefghijk"));
        Assert.Equal($"x{Mask}y", masker.Mask("xdefghijky"));
        Assert.Equal($"{Mask}z", masker.Mask("abcdefghz"));
    }

    [Fact]
    public async Task Where_one_value_is_the_beginning_of_another_the_longer_is_masked_whole()
    {
        var masker = await BuiltOverAsync("abcdefgh", "abcdefghijkl");

        Assert.Equal(Mask, masker.Mask("abcdefghijkl"));
        Assert.Equal($"{Mask}m", masker.Mask("abcdefghm"));
    }

    [Fact]
    public async Task A_secret_that_is_added_changed_or_removed_is_masked_or_released_without_a_restart()
    {
        var store = new FakeSecretStore();
        var masker = new SecretMasker(store);
        await masker.RebuildAsync(Token);
        Assert.Equal(Value, masker.Mask(Value));

        await store.SetAsync("token", Value, Token);
        Assert.Equal(Mask, masker.Mask(Value));

        await store.SetAsync("token", "another-value-0987654321", Token);
        Assert.Equal(Value, masker.Mask(Value));
        Assert.Equal(Mask, masker.Mask("another-value-0987654321"));

        await store.RemoveAsync("token", Token);
        Assert.Equal("another-value-0987654321", masker.Mask("another-value-0987654321"));
    }

    [Fact]
    public async Task A_stream_that_has_already_started_masks_a_secret_stored_after_it_began()
    {
        var store = new FakeSecretStore();
        var masker = new SecretMasker(store);
        await masker.RebuildAsync(Token);
        var stream = masker.CreateStream();
        var output = new StringBuilder(stream.Write("before "));

        await store.SetAsync("token", Value, Token);
        output.Append(stream.Write($"{Value} after")).Append(stream.Complete());

        Assert.Equal($"before {Mask} after", output.ToString());
    }

    [Theory]
    [MemberData(nameof(Forms))]
    public async Task A_value_split_between_two_chunks_leaves_no_part_of_it_in_the_output_wherever_it_is_split(string form)
    {
        var masker = await BuiltOverAsync(Value, "p@ss word/with+symbols=1");
        var text = $"head {form} tail";

        for (var split = 0; split <= text.Length; split++)
        {
            var stream = masker.CreateStream();
            var output = stream.Write(text.AsSpan(0, split)) + stream.Write(text.AsSpan(split)) + stream.Complete();

            Assert.Equal($"head {Mask} tail", output);
        }
    }

    [Theory]
    [MemberData(nameof(Forms))]
    public async Task A_value_delivered_one_character_at_a_time_is_masked(string form)
    {
        var masker = await BuiltOverAsync(Value, "p@ss word/with+symbols=1");
        var stream = masker.CreateStream();
        var output = new StringBuilder();

        foreach (var character in $"head {form} tail")
        {
            output.Append(stream.Write([character]));
            Assert.DoesNotContain(form[..Math.Min(form.Length, 2)], output.ToString().Replace(Mask, string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        }

        output.Append(stream.Complete());
        Assert.Equal($"head {Mask} tail", output.ToString());
    }

    [Fact]
    public async Task What_could_begin_a_secret_is_held_back_and_released_when_the_next_chunk_shows_it_is_not_one()
    {
        var masker = await BuiltOverAsync(Value);
        var stream = masker.CreateStream();

        var first = stream.Write("see sk-test-0123");
        var second = stream.Write("-not the secret");

        Assert.Equal("see ", first);
        Assert.Equal("sk-test-0123-not the secret", second);
        Assert.Equal(string.Empty, stream.Complete());
    }

    [Fact]
    public async Task What_is_still_held_when_the_stream_ends_is_written_by_complete()
    {
        var masker = await BuiltOverAsync(Value);
        var stream = masker.CreateStream();

        var written = stream.Write("tail sk-test-0123");
        var rest = stream.Complete();

        Assert.Equal("tail ", written);
        Assert.Equal("sk-test-0123", rest);
        Assert.Equal(string.Empty, stream.Complete());
    }

    [Fact]
    public async Task A_run_of_overlapping_values_that_a_chunk_ends_inside_is_not_written_until_it_is_known()
    {
        var masker = await BuiltOverAsync("abcdefgh", "defghijk");
        var stream = masker.CreateStream();

        var output = stream.Write("abcdefgh") + stream.Write("ij") + stream.Write("k!") + stream.Complete();

        Assert.Equal($"{Mask}!", output);
    }

    [Fact]
    public async Task A_value_that_text_around_it_does_not_complete_is_not_masked()
    {
        var masker = await BuiltOverAsync(Value);
        var stream = masker.CreateStream();

        var output = stream.Write("sk-test-0123456789-abcdefgh") + stream.Write("x") + stream.Complete();

        Assert.Equal("sk-test-0123456789-abcdefghx", output);
    }
}
