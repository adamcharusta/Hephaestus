using System.Reflection;
using System.Reflection.Emit;

namespace Hephaestus.Core.Tests;

public class HephaestusVersionTests
{
    [Fact]
    public void The_current_version_is_the_build_version_without_build_metadata()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$", HephaestusVersion.Current);
    }

    [Theory]
    [InlineData("1.4.0+abcdef", "1.4.0")]
    [InlineData("1.4.0-rc.1+abcdef", "1.4.0-rc.1")]
    [InlineData("2.0.0", "2.0.0")]
    public void Build_metadata_is_dropped(string informational, string expected)
    {
        Assert.Equal(expected, HephaestusVersion.Read(AssemblyWith(informational)));
    }

    [Fact]
    public void An_assembly_without_an_informational_version_uses_its_assembly_version()
    {
        Assert.Equal("3.2.1", HephaestusVersion.Read(AssemblyWith(informational: null, version: new Version(3, 2, 1, 0))));
    }

    private static Assembly AssemblyWith(string? informational, Version? version = null)
    {
        var builder = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("Fake") { Version = version ?? new Version(1, 0, 0, 0) }, AssemblyBuilderAccess.Run);
        if (informational is not null)
        {
            var constructor = typeof(AssemblyInformationalVersionAttribute).GetConstructor([typeof(string)])!;
            builder.SetCustomAttribute(new CustomAttributeBuilder(constructor, [informational]));
        }

        return builder;
    }
}
