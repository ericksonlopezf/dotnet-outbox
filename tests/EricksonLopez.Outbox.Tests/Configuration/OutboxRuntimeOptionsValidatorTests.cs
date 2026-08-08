using AwesomeAssertions;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Configuration;

public class OutboxRuntimeOptionsValidatorTests
{
    private readonly OutboxRuntimeOptionsValidator _validator = new();

    [Fact]
    public void Validate_ValidOptions_ReturnsSuccess()
    {
        var options = new OutboxRuntimeOptions
        {
            SchemaName = "valid_schema-123",
            TableName = "valid_table-123"
        };

        var result = _validator.Validate("test", options);

        result.Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData((string)null!)]
    [InlineData("invalid schema!")]
    [InlineData("schema;drop table")]
    public void Validate_InvalidSchemaName_ReturnsFail(string? invalidSchemaName)
    {
        var options = new OutboxRuntimeOptions
        {
            SchemaName = invalidSchemaName!,
            TableName = "valid_table"
        };

        var result = _validator.Validate("test", options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("SchemaName");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData((string)null!)]
    [InlineData("invalid table!")]
    [InlineData("table;drop database")]
    public void Validate_InvalidTableName_ReturnsFail(string? invalidTableName)
    {
        var options = new OutboxRuntimeOptions
        {
            SchemaName = "valid_schema",
            TableName = invalidTableName!
        };

        var result = _validator.Validate("test", options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("TableName");
    }
}
