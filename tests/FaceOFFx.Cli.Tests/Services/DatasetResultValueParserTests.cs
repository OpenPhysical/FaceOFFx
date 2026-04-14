using System.Text.Json;
using FaceOFFx.Cli.Services;
using FluentAssertions;
using NUnit.Framework;

namespace FaceOFFx.Cli.Tests.Services;

[TestFixture]
public class DatasetResultValueParserTests
{
    [Test]
    public void TryGetDouble_ReadsJsonNumberAndStringValues()
    {
        var numericElement = JsonDocument.Parse("0.75").RootElement.Clone();
        var stringElement = JsonDocument.Parse("\"0.65\"").RootElement.Clone();

        DatasetResultValueParser.TryGetDouble(numericElement).Should().Be(0.75d);
        DatasetResultValueParser.TryGetDouble(stringElement).Should().Be(0.65d);
        DatasetResultValueParser.TryGetDouble("0.55").Should().Be(0.55d);
    }

    [Test]
    public void TryGetString_ReadsJsonStringValues()
    {
        var stringElement = JsonDocument.Parse("\"LowSharpness\"").RootElement.Clone();

        DatasetResultValueParser.TryGetString(stringElement).Should().Be("LowSharpness");
        DatasetResultValueParser.TryGetString("LowOverallQuality").Should().Be("LowOverallQuality");
    }
}
