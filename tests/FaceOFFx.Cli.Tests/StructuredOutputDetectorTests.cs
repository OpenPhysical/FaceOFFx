using FaceOFFx.Cli;
using AwesomeAssertions;
using NUnit.Framework;

namespace FaceOFFx.Cli.Tests;

[TestFixture]
public class StructuredOutputDetectorTests
{
    [Test]
    public void RequiresCleanStdout_ReturnsTrueForDocumentJson()
    {
        StructuredOutputDetector.RequiresCleanStdout(new[] { "piv", "photo.jpg", "--json" }).Should().BeTrue();
        StructuredOutputDetector.RequiresCleanStdout(new[] { "us-passport", "photo.jpg", "--json" }).Should().BeTrue();
        StructuredOutputDetector.RequiresCleanStdout(new[] { "us-permanent-resident", "photo.jpg", "--json" }).Should().BeTrue();
        StructuredOutputDetector.RequiresCleanStdout(new[] { "canada-proof-of-citizenship", "photo.jpg", "--json" }).Should().BeTrue();
    }

    [Test]
    public void RequiresCleanStdout_ReturnsFalseForHumanReadableCommands()
    {
        StructuredOutputDetector.RequiresCleanStdout(new[] { "piv", "photo.jpg" }).Should().BeFalse();
        StructuredOutputDetector.RequiresCleanStdout(new[] { "documents" }).Should().BeFalse();
    }
}
