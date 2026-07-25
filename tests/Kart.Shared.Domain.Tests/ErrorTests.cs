using FluentAssertions;
using Kart.Shared.Domain;
using Xunit;

namespace Kart.Shared.Domain.Tests;

public class ErrorTests
{
    [Fact]
    public void None_HasEmptyCodeAndMessage()
    {
        Error.None.Code.Should().BeEmpty();
        Error.None.Message.Should().BeEmpty();
    }

    [Theory]
    [InlineData("validation_error")]
    public void Validation_UsesTheValidationErrorCode(string expectedCode)
    {
        var error = Error.Validation("bad input");

        error.Code.Should().Be(expectedCode);
        error.Message.Should().Be("bad input");
    }

    [Fact]
    public void NotFound_UsesTheNotFoundCode()
    {
        Error.NotFound("missing").Code.Should().Be("not_found");
    }

    [Fact]
    public void Conflict_UsesTheConflictCode()
    {
        Error.Conflict("already exists").Code.Should().Be("conflict");
    }

    [Fact]
    public void Unauthorized_UsesTheUnauthorizedCode()
    {
        Error.Unauthorized("no access").Code.Should().Be("unauthorized");
    }

    [Fact]
    public void Custom_UsesWhateverCodeTheCallerSupplies()
    {
        var error = Error.Custom("max_depth_exceeded", "too deep");

        error.Code.Should().Be("max_depth_exceeded");
        error.Message.Should().Be("too deep");
    }
}
