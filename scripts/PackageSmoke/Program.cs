using FaceOFFx;
using System.Text.Json;

if (args.Length != 1) throw new ArgumentException("Supply the source portrait path.");
using var encoder = new PivImageEncoder();
foreach (var target in new[] { PivFileSizeTarget.Minimum, PivFileSizeTarget.Preferred, PivFileSizeTarget.FromBytes(16_000) })
{
    var result = await encoder.EncodeFileAsync(args[0], target);
    if (result.IsFailure) throw new InvalidOperationException(result.Error.Message);
    if (result.Value.ImageData.Length == 0 || result.Value.ImageData.Length > target.MaximumBytes)
        throw new InvalidOperationException("Encoding failed the requested JP2 byte cap.");
    if (result.Value.Encoding.CodecEvidence?.PayloadTelemetry?.TotalOutputBytes != result.Value.ImageData.Length)
        throw new InvalidOperationException("The package consumer could not reconcile the emitted payload.");
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        Runtime = Environment.Version.ToString(),
        Assembly = typeof(PivImageEncoder).Assembly.GetName().Version?.ToString(),
        Bytes = result.Value.ImageData.Length,
        Target = target.MaximumBytes,
        result.Value.OutputDimensions,
        result.Value.Encoding.RegionalCompressionVerification
    }));
}
