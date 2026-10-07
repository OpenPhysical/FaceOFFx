using System.Buffers.Binary;
using System.Text;
using SixLabors.ImageSharp.PixelFormats;

namespace FaceOFFx.Infrastructure.Services;

// ICC.1:2022 sections 8.3, 8.10, 10.6, 10.18 and Annex F define this narrow matrix/TRC model.
internal sealed class PivRgbMatrixProfile
{
    // ICC registry sRGB.pdf page 4, Bradford-adapted D50 colorants.
    private static readonly double[] SrgbMatrix =
    [
        .436030342570117, .385101860087134, .143067806654203,
        .222438466210245, .716942745571917, .060618777416563,
        .013897440074263, .097076381494207, .713926257896652
    ];
    private static readonly double[] InverseSrgbMatrix = Invert(SrgbMatrix);
    private readonly double[][] _curves;
    private readonly double[] _sourceToSrgb;

    internal bool DescribesSrgb { get; }

    private PivRgbMatrixProfile(double[] matrix, double[][] curves)
    {
        _curves = curves;
        _sourceToSrgb = Multiply(InverseSrgbMatrix, matrix);
        DescribesSrgb = matrix.Zip(SrgbMatrix).All(pair => Math.Abs(pair.First - pair.Second) <= .0002) &&
                       curves.All(curve => curve.Select((value, index) =>
                           Math.Abs(value - DecodeSrgb(index / 255d))).All(error => error <= .0001));
    }

    internal static PivRgbMatrixProfile Parse(byte[] bytes, int maximumBytes)
    {
        if (bytes.Length < 132 || bytes.Length > maximumBytes || U32(bytes, 0) != bytes.Length)
            throw Invalid("ICC declared size must match the bounded profile payload.");
        if (Signature(bytes, 36) != "acsp" || bytes[8] is not (2 or 4))
            throw Invalid("Supported ICC headers use the acsp signature and profile version 2 or 4.");
        if (Signature(bytes, 12) is not ("mntr" or "scnr" or "spac") ||
            Signature(bytes, 16) != "RGB " || Signature(bytes, 20) != "XYZ ")
            throw Invalid("Supported source ICC profiles use an input, display, or color-space RGB matrix with XYZ PCS.");
        if (U32(bytes, 64) > 3 || bytes.AsSpan(100, 28).ContainsAnyExcept((byte)0))
            throw Invalid("ICC rendering-intent and reserved-header fields require valid values.");
        var pcsWhite = new[] { Fixed(bytes, 68), Fixed(bytes, 72), Fixed(bytes, 76) };
        if (Math.Abs(pcsWhite[0] - .9642) > .0001 || Math.Abs(pcsWhite[1] - 1) > .0001 || Math.Abs(pcsWhite[2] - .8249) > .0001)
            throw Invalid("ICC PCS illuminant must be D50.");

        var count = U32(bytes, 128);
        if (count is 0 or > 128 || 132L + 12L * count > bytes.Length)
            throw Invalid("ICC tag count exceeds the validated table bounds.");
        var tags = new Dictionary<string, Tag>(StringComparer.Ordinal);
        var tableEnd = 132L + 12L * count;
        for (var index = 0; index < count; index++)
        {
            var position = checked(132 + 12 * index);
            var offset = U32(bytes, position + 4);
            var length = U32(bytes, position + 8);
            if (offset < tableEnd || offset % 4 != 0 || length < 8 || (long)offset + length > bytes.Length)
                throw Invalid("ICC tag storage must be aligned and contained after its tag table.");
            var tag = new Tag(checked((int)offset), checked((int)length));
            if (!tags.TryAdd(Signature(bytes, position), tag)) throw Invalid("ICC tag signatures must be unique.");
        }
        var ranges = tags.Values.Distinct().OrderBy(tag => tag.Offset).ToArray();
        for (var index = 1; index < ranges.Length; index++)
            if (ranges[index].Offset < (long)ranges[index - 1].Offset + ranges[index - 1].Length)
                throw Invalid("ICC tags may share identical storage; partial overlaps require correction.");

        // ICC transform precedence requires a real LUT engine for these profiles.
        if (tags.Keys.Any(name => name.StartsWith("A2B", StringComparison.Ordinal) ||
                                  name.StartsWith("B2A", StringComparison.Ordinal) ||
                                  name.StartsWith("D2B", StringComparison.Ordinal) ||
                                  name.StartsWith("B2D", StringComparison.Ordinal) || name == "cicp"))
            throw Invalid("ICC LUT, multi-process, or CICP profiles require an external color-managed conversion to sRGB.");

        var red = Xyz(bytes, Required(tags, "rXYZ"));
        var green = Xyz(bytes, Required(tags, "gXYZ"));
        var blue = Xyz(bytes, Required(tags, "bXYZ"));
        _ = Xyz(bytes, Required(tags, "wtpt"));
        var matrix = new[] { red[0], green[0], blue[0], red[1], green[1], blue[1], red[2], green[2], blue[2] };
        // PCS-adapted, white-normalized matrices avoid guessing historical v2 adaptation conventions.
        if (Math.Abs(matrix[0] + matrix[1] + matrix[2] - .9642) > .005 ||
            Math.Abs(matrix[3] + matrix[4] + matrix[5] - 1) > .005 ||
            Math.Abs(matrix[6] + matrix[7] + matrix[8] - .8249) > .005)
            throw Invalid("ICC matrix colorants must already be normalized to the D50 PCS white.");
        _ = Invert(matrix);
        return new PivRgbMatrixProfile(matrix,
        [
            Curve(bytes, Required(tags, "rTRC")), Curve(bytes, Required(tags, "gTRC")),
            Curve(bytes, Required(tags, "bTRC"))
        ]);
    }

    internal Rgba32 Convert(Rgba32 pixel)
    {
        var red = _curves[0][pixel.R];
        var green = _curves[1][pixel.G];
        var blue = _curves[2][pixel.B];
        return new Rgba32(
            Encode(_sourceToSrgb[0] * red + _sourceToSrgb[1] * green + _sourceToSrgb[2] * blue),
            Encode(_sourceToSrgb[3] * red + _sourceToSrgb[4] * green + _sourceToSrgb[5] * blue),
            Encode(_sourceToSrgb[6] * red + _sourceToSrgb[7] * green + _sourceToSrgb[8] * blue), pixel.A);
    }

    private static double[] Xyz(byte[] bytes, Tag tag)
    {
        if (tag.Length != 20 || Signature(bytes, tag.Offset) != "XYZ " || U32(bytes, tag.Offset + 4) != 0)
            throw Invalid("ICC colorants and white point require a single XYZ value.");
        var result = new[] { Fixed(bytes, tag.Offset + 8), Fixed(bytes, tag.Offset + 12), Fixed(bytes, tag.Offset + 16) };
        if (result.Any(value => Math.Abs(value) > 4)) throw Invalid("ICC XYZ values exceed this converter's supported bounds.");
        return result;
    }

    private static double[] Curve(byte[] bytes, Tag tag)
    {
        if (tag.Length < 12 || U32(bytes, tag.Offset + 4) != 0)
            throw Invalid("ICC tone curve requires a valid type header.");
        var type = Signature(bytes, tag.Offset);
        var result = new double[256];
        if (type == "curv")
        {
            var count = U32(bytes, tag.Offset + 8);
            if (count > 65_536 || 12L + 2L * count != tag.Length)
                throw Invalid("ICC sampled curve count must match its bounded tag payload.");
            var gamma = count == 1 ? U16(bytes, tag.Offset + 12) / 256d : 1;
            if (gamma is <= 0 or > 16) throw Invalid("ICC gamma must lie in the supported range (0,16].");
            for (var index = 1; index < count; index++)
                if (U16(bytes, tag.Offset + 12 + 2 * index) < U16(bytes, tag.Offset + 10 + 2 * index))
                    throw Invalid("ICC sampled tone curves must be monotone increasing.");
            for (var index = 0; index < result.Length; index++)
            {
                var x = index / 255d;
                if (count < 2) result[index] = Math.Pow(x, gamma);
                else
                {
                    var sample = x * (count - 1);
                    var low = (int)sample;
                    var high = Math.Min(low + 1, (int)count - 1);
                    var fraction = sample - low;
                    result[index] = ((1 - fraction) * U16(bytes, tag.Offset + 12 + 2 * low) +
                                      fraction * U16(bytes, tag.Offset + 12 + 2 * high)) / 65535d;
                }
            }
        }
        else if (type == "para")
        {
            var function = U16(bytes, tag.Offset + 8);
            var parameterCount = function switch { 0 => 1, 1 => 3, 2 => 4, 3 => 5, 4 => 7, _ => 0 };
            if (parameterCount == 0 || U16(bytes, tag.Offset + 10) != 0 || tag.Length != 12 + 4 * parameterCount)
                throw Invalid("ICC parametric curve type and parameter storage require valid values.");
            var values = new double[7];
            for (var index = 0; index < parameterCount; index++) values[index] = Fixed(bytes, tag.Offset + 12 + 4 * index);
            if (values[0] is <= 0 or > 16 || values.Any(value => Math.Abs(value) > 16) ||
                function > 0 && values[1] <= 0 || function >= 3 && (values[4] < 0 || values[4] > 1 || values[3] < 0 ||
                    values[1] * values[4] + values[2] < -1e-12))
                throw Invalid("ICC parametric curve parameters require a defined monotone function over [0,1].");
            for (var index = 0; index < result.Length; index++)
            {
                var x = index / 255d;
                var powered = Math.Pow(Math.Max(0, values[1] * x + values[2]), values[0]);
                // ICC.1:2022 Table 68 corrects the 2010 type4 offset and low-branch inequalities.
                result[index] = function switch
                {
                    0 => Math.Pow(x, values[0]),
                    1 => x >= -values[2] / values[1] ? powered : 0,
                    2 => x >= -values[2] / values[1] ? powered + values[3] : values[3],
                    3 => x >= values[4] ? powered : values[3] * x,
                    _ => x >= values[4] ? powered + values[5] : values[3] * x + values[6]
                };
                result[index] = Math.Clamp(result[index], 0, 1);
            }
        }
        else throw Invalid("Supported ICC tone curves use curveType or parametricCurveType.");
        if (result.Any(value => !double.IsFinite(value)) || result[^1] - result[0] < .0001 ||
            result.Zip(result.Skip(1)).Any(pair => pair.First > pair.Second + 1e-12))
            throw Invalid("ICC tone curves require finite, nonconstant, monotone values.");
        return result;
    }

    private static double[] Multiply(double[] left, double[] right)
    {
        var result = new double[9];
        for (var row = 0; row < 3; row++)
        for (var column = 0; column < 3; column++)
        for (var index = 0; index < 3; index++)
            result[3 * row + column] += left[3 * row + index] * right[3 * index + column];
        return result;
    }

    private static double[] Invert(double[] m)
    {
        var determinant = m[0] * (m[4] * m[8] - m[5] * m[7]) - m[1] * (m[3] * m[8] - m[5] * m[6]) +
                          m[2] * (m[3] * m[7] - m[4] * m[6]);
        if (Math.Abs(determinant) < .000001) throw Invalid("ICC colorant matrix must be nonsingular.");
        return new[]
        {
            (m[4] * m[8] - m[5] * m[7]) / determinant, (m[2] * m[7] - m[1] * m[8]) / determinant, (m[1] * m[5] - m[2] * m[4]) / determinant,
            (m[5] * m[6] - m[3] * m[8]) / determinant, (m[0] * m[8] - m[2] * m[6]) / determinant, (m[2] * m[3] - m[0] * m[5]) / determinant,
            (m[3] * m[7] - m[4] * m[6]) / determinant, (m[1] * m[6] - m[0] * m[7]) / determinant, (m[0] * m[4] - m[1] * m[3]) / determinant
        };
    }

    private static byte Encode(double linear)
    {
        linear = Math.Clamp(linear, 0, 1);
        var encoded = linear <= .0031308 ? 12.92 * linear : 1.055 * Math.Pow(linear, 1 / 2.4) - .055;
        return (byte)Math.Clamp((int)Math.Round(255 * encoded, MidpointRounding.AwayFromZero), 0, 255);
    }

    private static double DecodeSrgb(double encoded) => encoded <= .04045
        ? encoded / 12.92 : Math.Pow((encoded + .055) / 1.055, 2.4);
    private static Tag Required(Dictionary<string, Tag> tags, string name) => tags.TryGetValue(name, out var tag)
        ? tag : throw Invalid($"ICC matrix profile requires the {name} tag.");
    private static string Signature(byte[] bytes, int offset) => Encoding.ASCII.GetString(bytes, offset, 4);
    private static uint U32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
    private static ushort U16(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset, 2));
    private static double Fixed(byte[] bytes, int offset) => BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset, 4)) / 65536d;
    private static InvalidDataException Invalid(string message) => new(message);
    private readonly record struct Tag(int Offset, int Length);
}
