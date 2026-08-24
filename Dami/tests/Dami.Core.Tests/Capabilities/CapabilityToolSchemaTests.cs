using System.Text.Json;
using Dami.Contracts.Capabilities;
using Xunit;

namespace Dami.Core.Tests.Capabilities;

public sealed class CapabilityToolSchemaTests
{
    [Fact]
    public void Constructor_Should_Own_Parameter_Json_After_The_Source_Is_Disposed()
    {
        CapabilityToolSchema schema;
        using (var document = JsonDocument.Parse("""{"type":"object"}"""))
        {
            schema = new CapabilityToolSchema(
                Guid.NewGuid(), "read_file", "Read a file.", document.RootElement);
        }

        Assert.Equal("object", schema.Parameters.GetProperty("type").GetString());
    }

    [Theory]
    [InlineData("read file")]
    [InlineData("read.file")]
    [InlineData("écrire")]
    public void Constructor_Should_Reject_Non_Portable_Function_Names(string name)
    {
        var parameters = JsonSerializer.SerializeToElement(new { type = "object" });

        Assert.Throws<ArgumentException>(() => new CapabilityToolSchema(
            Guid.NewGuid(), name, "Read a file.", parameters));
    }

    [Fact]
    public void Constructor_Should_Reject_A_Non_Object_Argument_Schema()
    {
        var parameters = JsonSerializer.SerializeToElement(new { type = "array" });

        var exception = Assert.Throws<ArgumentException>(() => new CapabilityToolSchema(
            Guid.NewGuid(), "read_file", "Read a file.", parameters));

        Assert.Equal("parameters", exception.ParamName);
    }
}
