using AwesomeAssertions;
using FaceOFFx.Cli.Commands;
using FaceOFFx.Tests.Common;
using NUnit.Framework;

namespace FaceOFFx.Cli.Tests.Commands;

[TestFixture]
public class PivOutputWriterTests : IntegrationTestBase
{
    [Test]
    public async Task NewPair_CommitsBothStagedFilesAndClearsTemporaryFiles()
    {
        var output = Path.Combine(TempDirectory, "portrait.jp2");
        await PivOutputWriter.WriteAsync(output, [1, 2], [3, 4], false);
        (await File.ReadAllBytesAsync(output)).Should().Equal([1, 2]);
        (await File.ReadAllBytesAsync(output + ".json")).Should().Equal([3, 4]);
        Directory.GetFiles(TempDirectory).Should().HaveCount(2);
    }

    [Test]
    public async Task FailedSecondCommit_RestoresBothExistingFiles()
    {
        var output = Path.Combine(TempDirectory, "portrait.jp2");
        await File.WriteAllBytesAsync(output, [5, 6]);
        await File.WriteAllBytesAsync(output + ".json", [7, 8]);
        Assert.ThrowsAsync<IOException>(() => PivOutputWriter.WriteAsync(output, [1, 2], [3, 4], true,
            beforeEvidenceCommit: () => throw new IOException("Injected second-commit failure")));
        (await File.ReadAllBytesAsync(output)).Should().Equal([5, 6]);
        (await File.ReadAllBytesAsync(output + ".json")).Should().Equal([7, 8]);
        Directory.GetFiles(TempDirectory).Should().HaveCount(2);
    }

    [Test]
    public async Task ExistingPair_DefaultWritePreservesEveryByte()
    {
        var output = Path.Combine(TempDirectory, "portrait.jp2");
        await File.WriteAllBytesAsync(output, [5, 6]);
        Assert.ThrowsAsync<IOException>(() => PivOutputWriter.WriteAsync(output, [1, 2], [3, 4], false));
        (await File.ReadAllBytesAsync(output)).Should().Equal([5, 6]);
        File.Exists(output + ".json").Should().BeFalse();
    }

    [Test]
    public async Task SymbolicLinkOutput_PreservesItsSourceTargetDuringOverwrite()
    {
        if (OperatingSystem.IsWindows()) Assert.Ignore("Symbolic-link creation requires host privileges on Windows.");
        var source = Path.Combine(TempDirectory, "source.jpg");
        var output = Path.Combine(TempDirectory, "portrait.jp2");
        await File.WriteAllBytesAsync(source, [5, 6]);
        File.CreateSymbolicLink(output, source);
        Assert.ThrowsAsync<IOException>(() => PivOutputWriter.WriteAsync(output, [1, 2], [3, 4], true));
        (await File.ReadAllBytesAsync(source)).Should().Equal([5, 6]);
    }

    [Test]
    public async Task ExistingLock_RemainsUntouchedWithActionableFailure()
    {
        var output = Path.Combine(TempDirectory, "portrait.jp2");
        var lockPath = output + ".write-lock";
        await File.WriteAllBytesAsync(lockPath, [5, 6]);
        var exception = Assert.ThrowsAsync<IOException>(() => PivOutputWriter.WriteAsync(output, [1, 2], [3, 4], true));
        exception!.Message.Should().Contain("review and remove a stale lock");
        (await File.ReadAllBytesAsync(lockPath)).Should().Equal([5, 6]);
        Directory.GetFiles(TempDirectory).Should().HaveCount(1);
    }

    [Test]
    public async Task DestinationCreatedDuringStaging_PreservesItsBytes()
    {
        var output = Path.Combine(TempDirectory, "portrait.jp2");
        Assert.ThrowsAsync<IOException>(() => PivOutputWriter.WriteAsync(output, [1, 2], [3, 4], false,
            beforeOutputCommit: () => File.WriteAllBytes(output, [5, 6])));
        (await File.ReadAllBytesAsync(output)).Should().Equal([5, 6]);
        Directory.GetFiles(TempDirectory).Should().HaveCount(1);
    }

    [Test]
    public async Task SymbolicLinkCreatedDuringStaging_PreservesItsSourceTarget()
    {
        if (OperatingSystem.IsWindows()) Assert.Ignore("Symbolic-link creation requires host privileges on Windows.");
        var source = Path.Combine(TempDirectory, "source.jpg");
        var output = Path.Combine(TempDirectory, "portrait.jp2");
        await File.WriteAllBytesAsync(source, [5, 6]);
        Assert.ThrowsAsync<IOException>(() => PivOutputWriter.WriteAsync(output, [1, 2], [3, 4], true,
            beforeOutputCommit: () => File.CreateSymbolicLink(output, source)));
        (await File.ReadAllBytesAsync(source)).Should().Equal([5, 6]);
        Directory.GetFiles(TempDirectory).Should().HaveCount(2);
    }
}
