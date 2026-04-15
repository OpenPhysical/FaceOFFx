using FaceOFFx.Cli;
using AwesomeAssertions;
using NUnit.Framework;

namespace FaceOFFx.Cli.Tests;

[TestFixture]
public class StructuredOutputDetectorTests
{
    [Test]
    public void RequiresCleanStdout_ReturnsTrueForQualityJson()
    {
        StructuredOutputDetector.RequiresCleanStdout(new[] { "quality", "--format", "json" }).Should().BeTrue();
        StructuredOutputDetector.RequiresCleanStdout(new[] { "--debug", "quality", "--format", "json" }).Should().BeTrue();
        StructuredOutputDetector.RequiresCleanStdout(new[] { "quality", "--format=json" }).Should().BeTrue();
    }

    [Test]
    public void RequiresCleanStdout_ReturnsFalseForHumanReadableCommands()
    {
        StructuredOutputDetector.RequiresCleanStdout(new[] { "quality", "--format", "detailed" }).Should().BeFalse();
        StructuredOutputDetector.RequiresCleanStdout(new[] { "validate", "--format", "json" }).Should().BeFalse();
    }
}
