using AwesomeAssertions;
using NUnit.Framework;

namespace FaceOFFx.Cli.Tests.Commands;

[TestFixture]
public class DocumentCommandTests
{
    [Test]
    public void Help_DescribesTheImageByteTargetAndOutputControls()
    {
        var result = CliTestHarness.Create().Run("--help");
        result.ExitCode.Should().Be(0);
        result.Output.Should().Contain("--filesize-target").And.Contain("--output").And.Contain("--overwrite");
        result.Output.Should().NotContain("--luma-utility").And.NotContain("--start-level").And.NotContain("--block-size");
        result.Output.Should().NotContain("us-passport").And.NotContain("canada-passport");
    }

    [TestCase("us-passport")]
    [TestCase("us-permanent-resident")]
    [TestCase("canada-passport")]
    [TestCase("canada-permanent-resident")]
    [TestCase("canada-citizenship-grant")]
    [TestCase("canada-proof-of-citizenship")]
    [TestCase("documents")]
    [TestCase("process")]
    [TestCase("quality")]
    [TestCase("validate")]
    public async Task ReleaseSurface_AcceptsPivInputsAndRejectsFormerCommands(string command)
    {
        (await CliProcessHarness.Run(command)).ExitCode.Should().NotBe(0);
    }
}
