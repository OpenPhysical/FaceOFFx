namespace FaceOFFx.Cli.Commands;

internal static class PivOutputWriter
{
    public static async Task WriteAsync(string output, byte[] image, byte[] evidence, bool overwrite,
        CancellationToken cancellationToken = default, Action? beforeEvidenceCommit = null,
        Action? beforeOutputCommit = null)
    {
        var evidencePath = output + ".json";
        ValidateTargets(output, evidencePath, overwrite);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        var token = Guid.NewGuid().ToString("N");
        var stageImage = output + ".stage." + token;
        var stageEvidence = evidencePath + ".stage." + token;
        var backupImage = output + ".backup." + token;
        var backupEvidence = evidencePath + ".backup." + token;
        var lockPath = output + ".write-lock";
        FileStream writeLock;
        try
        {
            writeLock = new FileStream(lockPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        }
        catch (IOException exception) when (File.Exists(lockPath) || Directory.Exists(lockPath) || new FileInfo(lockPath).LinkTarget is not null)
        {
            throw new IOException($"Output lock exists at {lockPath}. Wait for its writer, or review and remove a stale lock before retrying.", exception);
        }
        var imageCommitted = false;
        var evidenceCommitted = false;
        try
        {
            await Stage(stageImage, image, cancellationToken).ConfigureAwait(false);
            await Stage(stageEvidence, evidence, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            beforeOutputCommit?.Invoke();
            // The lock serializes cooperating writers; check destinations again after staging.
            ValidateTargets(output, evidencePath, overwrite);
            if (overwrite && File.Exists(output)) File.Move(output, backupImage);
            if (overwrite && File.Exists(evidencePath)) File.Move(evidencePath, backupEvidence);
            File.Move(stageImage, output);
            imageCommitted = true;
            beforeEvidenceCommit?.Invoke();
            File.Move(stageEvidence, evidencePath);
            evidenceCommitted = true;
        }
        catch
        {
            // Restore the previous pair after a failed commit, retaining externally changed files.
            if (imageCommitted) RemoveOwnedOutput(output, image);
            if (evidenceCommitted) RemoveOwnedOutput(evidencePath, evidence);
            if (File.Exists(backupImage)) File.Move(backupImage, output);
            if (File.Exists(backupEvidence)) File.Move(backupEvidence, evidencePath);
            throw;
        }
        finally
        {
            if (File.Exists(stageImage)) File.Delete(stageImage);
            if (File.Exists(stageEvidence)) File.Delete(stageEvidence);
            if (imageCommitted && evidenceCommitted)
            {
                if (File.Exists(backupImage)) File.Delete(backupImage);
                if (File.Exists(backupEvidence)) File.Delete(backupEvidence);
            }
            await writeLock.DisposeAsync().ConfigureAwait(false);
            File.Delete(lockPath);
        }
    }

    private static void ValidateTargets(string output, string evidencePath, bool overwrite)
    {
        if (new FileInfo(output).LinkTarget is not null || new FileInfo(evidencePath).LinkTarget is not null)
            throw new IOException("Choose regular output paths; symbolic links require a separate destination.");
        if (Directory.Exists(output) || Directory.Exists(evidencePath))
            throw new IOException("Choose output file paths separate from existing directories.");
        if (!overwrite && (File.Exists(output) || File.Exists(evidencePath)))
            throw new IOException("Choose a new output pair or authorize --overwrite.");
    }

    private static async Task Stage(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await file.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await file.FlushAsync(cancellationToken).ConfigureAwait(false);
        file.Flush(flushToDisk: true);
    }

    private static void RemoveOwnedOutput(string path, byte[] expected)
    {
        if (!File.Exists(path)) return;
        if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(expected))
            throw new IOException($"Output changed during recovery. Preserve the adjacent .backup files for {path}.");
        File.Delete(path);
    }
}
