using FaceOFFx.Cli;
using AwesomeAssertions;
using NUnit.Framework;

namespace FaceOFFx.Cli.Tests;

[TestFixture]
public class StructuredOutputDetectorTests
{
    [Test]
    public void RequiresCleanStdout_ReturnsTrueForPrimaryAndAliasJson()
    {
        StructuredOutputDetector.RequiresCleanStdout(new[] { "photo.jpg", "--json" }).Should().BeTrue();
        StructuredOutputDetector.RequiresCleanStdout(new[] { "piv", "photo.jpg", "--json" }).Should().BeTrue();
    }

    [Test]
    public void RequiresCleanStdout_ReturnsFalseForHumanReadableCommands()
    {
        StructuredOutputDetector.RequiresCleanStdout(new[] { "piv", "photo.jpg" }).Should().BeFalse();
        StructuredOutputDetector.RequiresCleanStdout(new[] { "photo.jpg" }).Should().BeFalse();
    }
}
