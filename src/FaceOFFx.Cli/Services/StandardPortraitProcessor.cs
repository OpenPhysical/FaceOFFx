using CSharpFunctionalExtensions;
using FaceOFFx.Infrastructure.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FaceOFFx.Cli.Services;

internal sealed class StandardPortraitProcessor(StandardPortraitProcessorService processor)
{
    private readonly StandardPortraitProcessorService _processor = processor;

    public Task<Result<StandardPortraitResult>> ProcessAsync(
        Image<Rgba32> sourceImage,
        string standardName,
        float minConfidence,
        int minFaceSize,
        CancellationToken cancellationToken = default)
    {
        return _processor.ProcessAsync(
            sourceImage,
            standardName,
            minConfidence,
            minFaceSize,
            cancellationToken);
    }
}
