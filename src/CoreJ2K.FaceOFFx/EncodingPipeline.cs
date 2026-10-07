// Copyright (c) 2007-2016 CSJ2K contributors.
// Licensed under the BSD 3-Clause License. See LICENSE and COPYRIGHT-JJ2000-5.1.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CoreJ2K.FaceOFFx.Util;
using CoreJ2K.FaceOFFx.j2k.codestream.writer;
using CoreJ2K.FaceOFFx.j2k.encoder;
using CoreJ2K.FaceOFFx.j2k.entropy.encoder;
using CoreJ2K.FaceOFFx.j2k.fileformat.writer;
using CoreJ2K.FaceOFFx.j2k.image;
using CoreJ2K.FaceOFFx.j2k.image.forwcomptransf;
using CoreJ2K.FaceOFFx.j2k.io;
using CoreJ2K.FaceOFFx.j2k.quantization.quantizer;
using CoreJ2K.FaceOFFx.j2k.roi.encoder;
using CoreJ2K.FaceOFFx.j2k.util;
using CoreJ2K.FaceOFFx.j2k.wavelet.analysis;
namespace CoreJ2K.FaceOFFx
{
    internal static class EncodingPipeline
    {
        internal static EncoderResult Encode(BlkImgDataSrc source, EncoderOptions options)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            EncoderResult result = null;
            try
            {
                options.Validate(source);
                var bytes = ToBytesCore(source, options.ToParameterList(), options, out result);
                if (bytes == null || result == null)
                    throw new InvalidOperationException("The JPEG 2000 encoder could not produce the requested output.");
                return result;
            }
            finally
            {
                // The successful legacy pipeline closes its source before returning.
                if (result == null) source.Close();
            }
        }

#nullable disable

        private static byte[] ToBytesCore(BlkImgDataSrc imgsrc, ParameterList parameters,
            EncoderOptions options, out EncoderResult result)
        {
            result = null;
            if (imgsrc == null) throw new ArgumentNullException(nameof(imgsrc));
            // Initialize default parameters
            var defpl = GetDefaultEncoderParameterList(encoder_pinfo);

            // Create parameter list using defaults
            var pl = new ParameterList(defpl);
            if (parameters?.DefaultParameterList != null)
                foreach (var entry in parameters.DefaultParameterList) pl[entry.Key] = entry.Value;
            if (parameters != null)
                foreach (var entry in parameters) pl[entry.Key] = entry.Value;
            if (options?.MaximumOutputBytes != null)
                pl["Alayers"] = "sl";

            var componentDepths = new int[imgsrc.NumComps];
            var componentSignedness = new bool[imgsrc.NumComps];
            for (var component = 0; component < componentDepths.Length; component++)
            {
                componentDepths[component] = imgsrc.getNomRangeBits(component);
                componentSignedness[component] = options != null && imgsrc.IsOrigSigned(component);
            }
            var containerOverhead = pl.getParameter("file_format").Equals("on")
                ? new FileFormatWriter(Stream.Null, imgsrc.ImgHeight, imgsrc.ImgWidth,
                    imgsrc.NumComps, componentDepths, 0, componentSignedness).FileFormatOverheadBytes
                : 0;
            var maximumCodestreamBytes = options?.MaximumOutputBytes - containerOverhead;
            if (maximumCodestreamBytes is <= 0)
                throw new ArgumentException("The complete-output limit cannot hold the requested payload and headers.", nameof(options));

            var useFileFormat = false;
            var pphTile = false;
            var pphMain = false;

            // **** Get general parameters ****

            if (pl.getParameter("file_format").Equals("on"))
            {
                useFileFormat = true;
                if (pl.getParameter("rate") != null 
                    && pl.getFloatParameter("rate") != defpl.getFloatParameter("rate"))
                {
                    warning("Specified bit-rate applies only on the codestream but not on the whole file.");
                }
            }

            if (pl.getParameter("tiles") == null)
            {
                error("No tiles option specified", 2);
                return null;
            }

            if (pl.getParameter("pph_tile").Equals("on"))
            {
                pphTile = true;

                if (pl.getParameter("Psop").Equals("off"))
                {
                    pl["Psop"] = "on";
                }
                if (pl.getParameter("Peph").Equals("off"))
                {
                    pl["Peph"] = "on";
                }
            }

            if (pl.getParameter("pph_main").Equals("on"))
            {
                pphMain = true;

                if (pl.getParameter("Psop").Equals("off"))
                {
                    pl["Psop"] = "on";
                }
                if (pl.getParameter("Peph").Equals("off"))
                {
                    pl["Peph"] = "on";
                }
            }

            if (pphTile && pphMain) error("Can't have packed packet headers in both main and tile headers", 2);

            if (pl.getBooleanParameter("lossless") && pl.getParameter("rate") != null
                && pl.getFloatParameter("rate") != defpl.getFloatParameter("rate"))
                throw new ArgumentException("Cannot use '-rate' and '-lossless' option at  the same time.");

            if (pl.getParameter("rate") == null)
            {
                error("Target bitrate not specified", 2);
                return null;
            }
            float rate;
            try
            {
                rate = pl.getFloatParameter("rate");
                if (rate == -1)
                {
                    rate = float.MaxValue;
                }
            }
            catch (FormatException)
            {
                error($"Invalid value in 'rate' option: {pl.getParameter("rate")}", 2);
                return null;
            }
            int pktspertp;
            try
            {
                pktspertp = pl.getIntParameter("tile_parts");
                if (pktspertp != 0)
                {
                    if (pl.getParameter("Psop").Equals("off"))
                    {
                        pl["Psop"] = "on";
                    }
                    if (pl.getParameter("Peph").Equals("off"))
                    {
                        pl["Peph"] = "on";
                    }
                }
            }
            catch (FormatException)
            {
                error($"Invalid value in 'tile_parts' option: {pl.getParameter("tile_parts")}", 2);
                return null;
            }

            // **** ImgReader ****
            var ncomp = imgsrc.NumComps;
            var ppminput = imgsrc.NumComps > 1;

            // **** Tiler ****
            // get nominal tile dimensions
            var stok =
                new SupportClass.StreamTokenizerSupport(new StringReader(pl.getParameter("tiles")));
            stok.EOLIsSignificant(false);

            stok.NextToken();
            if (stok.ttype != SupportClass.StreamTokenizerSupport.TT_NUMBER)
            {
                error($"An error occurred while parsing the tiles option: {pl.getParameter("tiles")}", 2);
                return null;
            }
            var tw = (int)stok.nval;
            stok.NextToken();
            if (stok.ttype != SupportClass.StreamTokenizerSupport.TT_NUMBER)
            {
                error($"An error occurred while parsing the tiles option: {pl.getParameter("tiles")}", 2);
                return null;
            }
            var th = (int)stok.nval;

            // Get image reference point
            var refs = pl.getParameter("ref").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int refx;
            int refy;
            try
            {
                refx = int.Parse(refs[0]);
                refy = int.Parse(refs[1]);
            }
            catch (IndexOutOfRangeException)
            {
                throw new ArgumentException("Error while parsing 'ref' option");
            }
            catch (FormatException)
            {
                throw new ArgumentException("Invalid number type in 'ref' option");
            }
            if (refx < 0 || refy < 0)
            {
                throw new ArgumentException("Invalid value in 'ref' option ");
            }

            // Get tiling reference point
            var trefs = pl.getParameter("tref").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int trefx;
            int trefy;
            try
            {
                trefx = int.Parse(trefs[0]);
                trefy = int.Parse(trefs[1]);
            }
            catch (IndexOutOfRangeException)
            {
                throw new ArgumentException("Error while parsing 'tref' option");
            }
            catch (FormatException)
            {
                throw new ArgumentException("Invalid number type in 'tref' option");
            }
            if (trefx < 0 || trefy < 0 || trefx > refx || trefy > refy)
            {
                throw new ArgumentException("Invalid value in 'tref' option ");
            }

            // Instantiate tiler
            Tiler imgtiler;
            try
            {
                imgtiler = new Tiler(imgsrc, refx, refy, trefx, trefy, tw, th);
            }
            catch (ArgumentException e)
            {
                error($"Could not tile image{((e.Message != null) ? (":\n" + e.Message) : "")}", 2);
                return null;
            }
            var ntiles = imgtiler.getNumTiles();

            // **** Encoder specifications ****
            var encSpec = new EncoderSpecs(ntiles, ncomp, imgsrc, pl);

            // **** Component transformation ****
            if (ppminput && pl.getParameter("Mct") != null && pl.getParameter("Mct").Equals("off"))
            {
                FacilityManager.getMsgLogger()
                    .printmsg(
                        MsgLogger_Fields.WARNING,
                        "Input image is RGB and no color transform has "
                        + "been specified. Compression performance and "
                        + "image quality might be greatly degraded. Use "
                        + "the 'Mct' option to specify a color transform");
            }
            ForwCompTransf fctransf;
            try
            {
                fctransf = new ForwCompTransf(imgtiler, encSpec);
            }
            catch (ArgumentException e)
            {
                error(
                    $"Could not instantiate forward component transformation{((e.Message != null) ? (":\n" + e.Message) : "")}",
                    2);
                return null;
            }

            // **** ImgDataConverter ****
            var converter = new ImgDataConverter(fctransf);


            // **** ForwardWT ****
            ForwardWT dwt;
            try
            {
                dwt = ForwardWT.createInstance(converter, pl, encSpec);
            }
            catch (ArgumentException e)
            {
                error($"Could not instantiate wavelet transform{((e.Message != null) ? (":\n" + e.Message) : "")}", 2);
                return null;
            }

            // **** Quantizer ****
            Quantizer quant;
            try
            {
                quant = Quantizer.createInstance(dwt, encSpec);
            }
            catch (ArgumentException e)
            {
                error($"Could not instantiate quantizer{((e.Message != null) ? (":\n" + e.Message) : "")}", 2);
                return null;
            }

            options?.ValidateQuantizer(quant);

            // **** ROIScaler ****
            ROIScaler rois;
            try
            {
                rois = options?.Roi == null
                    ? ROIScaler.createInstance(quant, pl, encSpec)
                    : ROIScaler.createInstance(quant, pl, encSpec, options.Roi);
            }
            catch (ArgumentException e)
            {
                error($"Could not instantiate ROI scaler{((e.Message != null) ? (":\n" + e.Message) : "")}", 2);
                return null;
            }

            // **** EntropyCoder ****
            EntropyCoder ecoder;
            try
            {
                ecoder = EntropyCoder.createInstance(
                    rois,
                    pl,
                    encSpec.cblks,
                    encSpec.pss,
                    encSpec.bms,
                    encSpec.mqrs,
                    encSpec.rts,
                    encSpec.css,
                    encSpec.sss,
                    encSpec.lcs,
                    encSpec.tts);
                if (ecoder is StdEntropyCoder standardEntropyCoder)
                {
                    standardEntropyCoder.SubbandWeights = options?.SubbandWeights;
                    if (options?.AttributeSharedPayload == true)
                        standardEntropyCoder.SharedPayloadInfluence = new SynthesisEnergyInfluence(options.Roi,
                            imgsrc.ImgWidth, imgsrc.ImgHeight, imgsrc.ImgULX, imgsrc.ImgULY);
                }
            }
            catch (ArgumentException e)
            {
                error($"Could not instantiate entropy coder{((e.Message != null) ? (":\n" + e.Message) : "")}", 2);
                return null;
            }

            // **** CodestreamWriter ****
            using (var outStream = new MemoryStream())
            {
                CodestreamWriter bwriter;
                try
                {
                    // Rely on rate allocator to limit amount of data
                    bwriter = new FileCodestreamWriter(outStream, int.MaxValue);
                }
                catch (IOException e)
                {
                    error($"Could not open output file{((e.Message != null) ? (":\n" + e.Message) : "")}", 2);
                    return null;
                }

                // **** Rate allocator ****
                PostCompRateAllocator ralloc;
                try
                {
                    ralloc = PostCompRateAllocator.createInstance(ecoder, pl, rate, bwriter, encSpec);
                    if (maximumCodestreamBytes.HasValue)
                        ralloc.ConfigureBudget(maximumCodestreamBytes.Value, true);
                }
                catch (ArgumentException e)
                {
                    error($"Could not instantiate rate allocator{((e.Message != null) ? (":\n" + e.Message) : "")}", 2);
                    return null;
                }

                // **** HeaderEncoder ****
                var headenc = new HeaderEncoder(imgsrc, componentSignedness, dwt, imgtiler, encSpec, rois, ralloc, pl);
                ralloc.HeaderEncoder = headenc;

                // **** Write header to be able to estimate header overhead ****
                headenc.encodeMainHeader();

                // **** Initialize rate allocator, with proper header
                // overhead. This will also encode all the data ****
                ralloc.initialize();

                // **** Write header (final) ****
                headenc.reset();
                headenc.encodeMainHeader();

                // Insert header into the codestream
                bwriter.commitBitstreamHeader(headenc);

                // **** Now do the rate-allocation and write result ****
                ralloc.runAndWrite();

                // **** Done ****
                bwriter.close();

                // **** Calculate file length ****
                var fileLength = bwriter.Length;
                var codestreamLength = fileLength;

                // **** File Format ****
                if (useFileFormat)
                {
                    try
                    {
                        outStream.Seek(0, SeekOrigin.Begin);
                        var ffw = new FileFormatWriter(
                            outStream,
                            imgsrc.ImgHeight,
                            imgsrc.ImgWidth,
                            imgsrc.NumComps,
                            componentDepths,
                            fileLength,
                            componentSignedness);
                        fileLength += ffw.writeFileFormat();
                    }
                    catch (IOException e)
                    {
                        throw new InvalidOperationException($"Error while writing JP2 file format: {e.Message}");
                    }
                }

                var encoded = outStream.ToArray();
                if (options != null)
                {
                    if (options.MaximumOutputBytes is { } limit && encoded.Length > limit)
                        throw new InvalidOperationException("The encoded output exceeds its complete-output byte limit.");
                    var diagnostics = ralloc.BudgetDiagnostics;
                    long? protectedBytes = options.Roi != null && !options.Roi.AlignToCodeBlocks
                        ? diagnostics?.ProtectedMaxshiftPayloadBytes
                        : null;
                    result = new EncoderResult(encoded, codestreamLength, encoded.Length - codestreamLength,
                        options.Roi?.GetPixelCount(imgsrc.ImgWidth, imgsrc.ImgHeight) ?? 0,
                        protectedBytes,
                        protectedBytes.HasValue
                            ? "Emitted Maxshift protected coding-phase payload; packet, codestream and container headers and promoted coarse-resolution payload are accounted separately. Spatial regional attribution is available through the separately reported versioned synthesis-energy convention."
                            : "Protected coding-phase attribution requires a byte-budgeted Maxshift ROI. Biometric qualification requires acquisition, regional and record validation.",
                        diagnostics, ralloc.CreatePayloadTelemetry(codestreamLength, encoded.Length - codestreamLength),
                        ralloc.CreateSharedPayloadAttribution());
                }

                // **** Close image readers ***
                imgsrc.Close();

                return encoded;
            }
        }


        internal static ParameterList GetDefaultEncoderParameterList(string[][] pinfo)
        {
            var pl = new ParameterList();
            string[][] str;

            str = pinfo ?? encoder_pinfo;
            if (str != null) for (var i = str.Length - 1; i >= 0; i--) pl[str[i][0]] = str[i][3];

            str = ForwCompTransf.ParameterInfo;
            if (str != null) for (var i = str.Length - 1; i >= 0; i--) pl[str[i][0]] = str[i][3];

            str = AnWTFilter.ParameterInfo;
            if (str != null) for (var i = str.Length - 1; i >= 0; i--) pl[str[i][0]] = str[i][3];

            str = ForwardWT.ParameterInfo;
            if (str != null) for (var i = str.Length - 1; i >= 0; i--) pl[str[i][0]] = str[i][3];

            str = Quantizer.ParameterInfo;
            if (str != null) for (var i = str.Length - 1; i >= 0; i--) pl[str[i][0]] = str[i][3];

            str = ROIScaler.ParameterInfo;
            if (str != null) for (var i = str.Length - 1; i >= 0; i--) pl[str[i][0]] = str[i][3];

            str = EntropyCoder.ParameterInfo;
            if (str != null) for (var i = str.Length - 1; i >= 0; i--) pl[str[i][0]] = str[i][3];

            str = HeaderEncoder.ParameterInfo;
            if (str != null) for (var i = str.Length - 1; i >= 0; i--) pl[str[i][0]] = str[i][3];

            str = PostCompRateAllocator.ParameterInfo;
            if (str != null) for (var i = str.Length - 1; i >= 0; i--) pl[str[i][0]] = str[i][3];

            str = PktEncoder.ParameterInfo;
            if (str != null) for (var i = str.Length - 1; i >= 0; i--) pl[str[i][0]] = str[i][3];

            return pl;
        }

        internal static ParameterList GetDefaultEncoderParameterList()
        {
            return GetDefaultEncoderParameterList(encoder_pinfo);
        }
        


        private static string[][] encoder_pinfo =
            {
                new string[]
                    {
                        "debug", null,
                        "Print debugging messages when an error is encountered.",
                        "off"
                    },
                new string[]
                    {
                        "disable_jp2_extension", "[on|off]",
                        "JJ2000 automatically adds .jp2 extension when using 'file_format'"
                        + "option. This option disables it when on.",
                        "off"
                    },
                new string[]
                    {
                        "file_format", "[on|off]",
                        "Puts the JPEG 2000 codestream in a JP2 file format wrapper.",
                        "on"
                    },
                new string[]
                    {
                        "pph_tile", "[on|off]",
                        "Packs the packet headers in the tile headers.", "off"
                    },
                new string[]
                    {
                        "pph_main", "[on|off]",
                        "Packs the packet headers in the main header.", "off"
                    },
                new string[]
                    {
                        "pfile", "<filename of arguments file>",
                        "Loads the arguments from the specified file. Arguments that are "
                        + "specified on the command line override the ones from the file.\n"
                        + "The arguments file is a simple text file with one argument per "
                        + "line of the following form:\n"
                        + "  <argument name>=<argument value>\n"
                        + "If the argument is of boolean type (i.e. its presence turns a "
                        + "feature on), then the 'on' value turns it on, while the 'off' "
                        + "value turns it off. The argument name does not include the '-' "
                        + "or '+' character. Long lines can be broken into several lines "
                        + "by terminating them with '\'. Lines starting with '#' are "
                        + "considered as comments. This option is not recursive: any 'pfile' "
                        + "argument appearing in the file is ignored.",
                        null
                    },
                new string[]
                    {
                        "tile_parts", "<packets per tile-part>",
                        "This option specifies the maximum number of packets to have in "
                        + "one tile-part. 0 means include all packets in first tile-part "
                        + "of each tile",
                        "0"
                    },
                new string[]
                    {
                        "tiles", "<nominal tile width> <nominal tile height>",
                        "This option specifies the maximum tile dimensions to use. "
                        + "If both dimensions are 0 then no tiling is used.",
                        "0 0"
                    },
                new string[]
                    {
                        "ref", "<x> <y>",
                        "Sets the origin of the image in the canvas system. It sets the "
                        + "coordinate of the top-left corner of the image reference grid, "
                        + "with respect to the canvas origin",
                        "0 0"
                    },
                new string[]
                    {
                        "tref", "<x> <y>",
                        "Sets the origin of the tile partitioning on the reference grid, "
                        + "with respect to the canvas origin. The value of 'x' ('y') "
                        + "specified can not be larger than the 'x' one specified in the ref "
                        + "option.",
                        "0 0"
                    },
                new string[]
                    {
                        "rate", "<output bitrate in bpp>",
                        "This is the output bitrate of the codestream in bits per pixel."
                        + " When equal to -1, no image information (beside quantization "
                        + "effects) is discarded during compression.\n"
                        + "Note: In the case where '-file_format' option is used, the "
                        + "resulting file may have a larger bitrate.",
                        "-1"
                    },
                new string[]
                    {
                        "lossless", "[on|off]",
                        "Specifies a lossless compression for the encoder. This options"
                        + " is equivalent to use reversible quantization ('-Qtype "
                        + "reversible')"
                        + " and 5x3 wavelet filters pair ('-Ffilters w5x3'). Note that "
                        + "this option cannot be used with '-rate'. When this option is "
                        + "off, the quantization type and the filters pair is defined by "
                        + "'-Qtype' and '-Ffilters' respectively.",
                        "off"
                    },
                new string[]
                    {
                        "i", "<image file> [,<image file> [,<image file> ... ]]",
                        "Mandatory argument. This option specifies the name of the input "
                        + "image files. If several image files are provided, they have to be"
                        + " separated by commas in the command line. Supported formats are "
                        + "PGM (raw), PPM (raw) and PGX, "
                        + "which is a simple extension of the PGM file format for single "
                        + "component data supporting arbitrary bitdepths. If the extension "
                        + "is '.pgm', PGM-raw file format is assumed, if the extension is "
                        + "'.ppm', PPM-raw file format is assumed, otherwise PGX file "
                        + "format is assumed. PGM and PPM files are assumed to be 8 bits "
                        + "deep. A multi-component image can be specified by either "
                        + "specifying several PPM and/or PGX files, or by specifying one "
                        + "PPM file.",
                        null
                    },
                new string[]
                    {
                        "o", "<file name>",
                        "Mandatory argument. This option specifies the name of the output "
                        + "file to which the codestream will be written.",
                        null
                    },
                new string[]
                    {
                        "verbose", null,
                        "Prints information about the obtained bit stream.", "on"
                    },
                new string[]
                    {
                        "v", "[on|off]", "Prints version and copyright information.",
                        "off"
                    },
                new string[]
                    {
                        "u", "[on|off]",
                        "Prints usage information. "
                        + "If specified all other arguments (except 'v') are ignored",
                        "off"
                    },
            };



        /**
     * Prints the error message 'msg' to standard err, prepending "ERROR" to
     * it, and sets the exitCode to 'code'. An exit code different than 0
     * indicates that there were problems.
     *
     * @param msg The error message
     *
     * @param code The exit code to set
     * */

        private static void error(string msg, int code)
        {
            //exitCode = code;
            FacilityManager.getMsgLogger().printmsg(MsgLogger_Fields.ERROR, msg);
        }

        /**
         * Prints the warning message 'msg' to standard err, prepending "WARNING"
         * to it.
         *
         * @param msg The error message
         * */

        private static void warning(string msg)
        {
            FacilityManager.getMsgLogger().printmsg(MsgLogger_Fields.WARNING, msg);
        }

    }
}
