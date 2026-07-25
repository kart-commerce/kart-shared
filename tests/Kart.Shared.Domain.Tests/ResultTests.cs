using FluentAssertions;
using Kart.Shared.Domain;
using Xunit;

namespace Kart.Shared.Domain.Tests;

public class ResultTests
{
    [Fact]
    public void Success_IsSuccess_And_CarriesNoError()
    {
        var result = Result.Success();

        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        result.Error.Should().Be(Error.None);
    }

    [Fact]
    public void Failure_IsFailure_And_CarriesTheGivenError()
    {
        var error = Error.NotFound("category not found");

        var result = Result.Failure(error);

        result.IsFailure.Should().BeTrue();
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(error);
    }

    [Fact]
    public void GenericSuccess_ExposesValue()
    {
        var result = Result.Success(42);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void GenericFailure_AccessingValue_Throws()
    {
        var result = Result.Failure<int>(Error.Validation("bad input"));

        var act = () => result.Value;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Cannot access the value of a failed result.");
    }

    [Fact]
    public void Failure_WithNoError_ViolatesTheInvariant_AndThrows()
    {
        // A failed Result must always carry a real Error — Error.None is reserved for success.
        var act = () => Result.Failure(Error.None);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("A failed result must carry an error.");
    }

    [Fact]
    public void GenericFailure_WithNoError_ViolatesTheInvariant_AndThrows()
    {
        var act = () => Result.Failure<int>(Error.None);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("A failed result must carry an error.");
    }
}
