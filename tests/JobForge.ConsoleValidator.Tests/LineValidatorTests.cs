using JobForge.Core.Models;
using JobForge.Core.Validation.LineValidator;
using Xunit;

namespace JobForge.ConsoleValidator.Tests;

public class LineValidatorTests
{
    private readonly LineValidator _sut = new();

    [Fact]
    public void Validate_ValidLine_ReturnsNoErrors()
    {
        var errors = _sut.Validate("""{"id":"a","currency":"USD","amount":1}""");

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_NegativeAmount_ReturnsInvalidAmount()
    {
        var errors = _sut.Validate("""{"id":"a","currency":"USD","amount":-1}""");

        Assert.Equal([ValidationErrorType.InvalidAmount], errors);
    }

    [Fact]
    public void Validate_ZeroAmount_ReturnsInvalidAmount()
    {
        var errors = _sut.Validate("""{"id":"a","currency":"USD","amount":0}""");

        Assert.Equal([ValidationErrorType.InvalidAmount], errors);
    }

    [Fact]
    public void Validate_BlankIdAndUnsupportedCurrency_ReturnsBothErrors()
    {
        var errors = _sut.Validate("""{"id":"","currency":"GBP","amount":20}""");

        Assert.Equal(2, errors.Count);
        Assert.Contains(ValidationErrorType.InvalidId, errors);
        Assert.Contains(ValidationErrorType.UnsupportedCurrency, errors);
    }

    [Fact]
    public void Validate_MissingFields_ReturnsAllThreeFieldErrors()
    {
        var errors = _sut.Validate("{}");

        Assert.Equal(3, errors.Count);
        Assert.DoesNotContain(ValidationErrorType.MalformedJson, errors);
    }

    [Theory]
    [InlineData("""{"id":"a","currency":"USD","amount":""")]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("""{"id":"a","currency":"USD","amount":"abc"}""")]
    public void Validate_MalformedInput_ReturnsOnlyMalformedJson(string line)
    {
        var errors = _sut.Validate(line);

        Assert.Equal([ValidationErrorType.MalformedJson], errors);
    }

    [Theory]
    [InlineData("USD")]
    [InlineData("EUR")]
    [InlineData("AZN")]
    public void Validate_SupportedCurrency_IsAccepted(string currency)
    {
        var errors = _sut.Validate($$"""{"id":"a","currency":"{{currency}}","amount":1}""");

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("GBP")]
    [InlineData("1")]
    [InlineData("USD,EUR")]
    public void Validate_UnsupportedCurrency_IsRejected(string currency)
    {
        var errors = _sut.Validate($$"""{"id":"a","currency":"{{currency}}","amount":1}""");

        Assert.Equal([ValidationErrorType.UnsupportedCurrency], errors);
    }
}
