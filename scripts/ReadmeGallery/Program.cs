using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using FaceOFFx;
using FaceOFFx.Core.Domain.Detection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

var root = Directory.GetCurrentDirectory();
var output = Path.GetFullPath(args.Length > 0 ? args[0] : "docs/samples");
var jsonOptions = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };
jsonOptions.Converters.Add(new JsonStringEnumConverter());
var sources = JsonSerializer.Deserialize<Source[]>(File.ReadAllBytes("tests/readme-sources.json"), jsonOptions)!;
var records = new List<object>();
foreach (var source in sources)
{
    if (!File.Exists(source.Path))
        throw new FileNotFoundException($"Supply the pinned gallery source for {source.Id} before regeneration.", source.Path);
    if (string.IsNullOrWhiteSpace(source.ExpectedSha256) || Hash(File.ReadAllBytes(source.Path)) != source.ExpectedSha256)
        throw new InvalidDataException($"Verify the pinned source hash for {source.Id} before regeneration.");
}
Directory.CreateDirectory(Path.Combine(output, "source"));
Directory.CreateDirectory(Path.Combine(output, "piv"));
using var encoder = new PivImageEncoder();
foreach (var source in sources)
{
    var sourceBytes = await File.ReadAllBytesAsync(source.Path);
    var digest = Hash(sourceBytes);
    if (source.ExpectedSha256 is { } expected && expected != digest)
        throw new InvalidDataException($"Verify the pinned source hash for {source.Id}.");
    using var image = Image.Load<Rgba32>(sourceBytes);
    var icc = image.Metadata.IccProfile?.ToByteArray();
    var thumbnailPath = Path.Combine(output, "source", source.Id + ".png");
    using (var thumbnail = image.Clone(context => context.Resize(420, (int)Math.Round(image.Height * 420d / image.Width))))
        await thumbnail.SaveAsPngAsync(thumbnailPath);
    foreach (var (capacity, target) in new[] { ("Minimum", PivFileSizeTarget.Minimum), ("Preferred", PivFileSizeTarget.Preferred) })
    {
        var prefix = Path.Combine(output, "piv", source.Id + "_" + capacity.ToLowerInvariant());
        var result = await encoder.EncodeAsync(sourceBytes, target);
        if (result.IsFailure)
        {
            var rejected = new
            {
                source.Id, source.Label, SourcePath = source.Path, source.SourceKind,
                SourceSha256 = digest, SourceWidth = image.Width, SourceHeight = image.Height,
                SourceIccProfileSha256 = icc is null ? null : Hash(icc), SizeProfile = capacity,
                SourcePreview = Relative(thumbnailPath), Status = "ReviewRequired",
                ErrorCode = result.Error.Code, Error = result.Error.Message, MaximumJp2Bytes = target.MaximumBytes
            };
            WriteJson(prefix + ".json", rejected);
            records.Add(rejected);
            Console.WriteLine($"{source.Id} {capacity}: {result.Error.Message}");
            continue;
        }
        var encoded = result.Value;
        if (encoded.ImageData.Length > target.MaximumBytes)
            throw new InvalidDataException("The gallery output exceeded its selected JP2 target.");
        var roi = AnatomicalFaceRoi.Create(encoded.OutputLandmarks, encoded.OutputDimensions.Width,
            encoded.OutputDimensions.Height, PivFaceRegion.LandmarkFace);
        if (roi.IsFailure || roi.Value.Mask is null)
            throw new InvalidDataException("Reproduce the encoding face mask for the gallery overlay.");
        var mask = roi.Value.Mask;
        if (Hash(mask.ToArray()) != encoded.Encoding.CodecEvidence?.RoiMaskSha256)
            throw new InvalidDataException("The displayed face mask must match the actual encoder mask.");
        await File.WriteAllBytesAsync(prefix + ".jp2", encoded.ImageData);
        using var maskImage = new Image<L8>(mask.Width, mask.Height);
        for (var y = 0; y < mask.Height; y++)
        for (var x = 0; x < mask.Width; x++)
        {
            maskImage[x, y] = new L8(mask.Contains(x, y) ? (byte)255 : (byte)0);
        }
        await maskImage.SaveAsPngAsync(prefix + "_mask.png");
        var record = new
        {
            source.Id, source.Label, SourcePath = source.Path, source.SourceKind, SourceSha256 = digest,
            SourceWidth = image.Width, SourceHeight = image.Height,
            SourceIccProfileSha256 = icc is null ? null : Hash(icc), SizeProfile = capacity,
            SourcePreview = Relative(thumbnailPath), ImagePath = Relative(prefix + ".jp2"),
            PreviewPath = Relative(prefix + ".png"), RoiPreviewPath = Relative(prefix + "_roi.png"),
            MaskPath = Relative(prefix + "_mask.png"), MaskSha256 = Hash(mask.ToArray()),
            ImageSha256 = Hash(encoded.ImageData), Status = "EncodedWithinBudget",
            FileSizeBytes = encoded.ImageData.Length, encoded.OutputDimensions, MaximumJp2Bytes = target.MaximumBytes,
            Recipe = new { StartResolutionLevel = 4, CodeBlockSize = 64, LumaUtility = 1.25,
                FaceRegion = "LandmarkFace", AllocationFloor = "None" },
            encoded.GeometryEvidence, encoded.RoiCoverage, encoded.SourceColorEvidence, EncodingEvidence = encoded.Encoding,
            encoded.VerificationRequirements, PreviewStatus = "PendingIndependentDecode"
        };
        WriteJson(prefix + ".json", record);
        records.Add(record);
        Console.WriteLine($"{source.Id} {capacity}: {encoded.ImageData.Length} bytes");
    }
}
WriteJson(Path.Combine(output, "manifest.json"), new { SchemaVersion = 3,
    ProductionEntryPoint = "PivImageEncoder.EncodeAsync", Sources = "tests/readme-sources.json",
    PixelPreviewScope = "Decoded JP2 output; face overlay uses the identical coding mask.", Assets = records });
Console.WriteLine(Relative(Path.Combine(output, "manifest.json")));

string Relative(string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
void WriteJson(string path, object value) => File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(value, jsonOptions));
static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
internal sealed record Source(string Id, string Label, string Path, string SourceKind, string ExpectedSha256);
