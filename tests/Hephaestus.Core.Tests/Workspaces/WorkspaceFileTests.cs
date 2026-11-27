using Hephaestus.Core.Workspaces;

namespace Hephaestus.Core.Tests.Workspaces;

public class WorkspaceFileTests
{
    [Theory]
    [InlineData("instruction.md")]
    [InlineData("templates/review/instruction.md")]
    [InlineData("a b/c.d")]
    public void A_relative_path_inside_the_directory_is_accepted(string path)
    {
        Assert.Equal(path, new WorkspaceFile(path, []).Path);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/etc/passwd")]
    [InlineData("../outside")]
    [InlineData("a/../../outside")]
    [InlineData("a//b")]
    [InlineData("./a")]
    [InlineData("a/.")]
    [InlineData("a\\b")]
    [InlineData("a\nb")]
    [InlineData("trailing/")]
    public void A_path_that_could_leave_the_directory_or_is_not_a_plain_path_is_refused(string path)
    {
        Assert.Throws<ArgumentException>(() => new WorkspaceFile(path, []));
    }

    [Fact]
    public void A_missing_path_or_content_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new WorkspaceFile(null!, []));
        Assert.Throws<ArgumentNullException>(() => new WorkspaceFile("a", null!));
    }
}
