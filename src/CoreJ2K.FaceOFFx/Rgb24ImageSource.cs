/*
 *
 * Class: ImgReaderImageSharp
 *
 * Description: Image reader for ImageSharp Image types
 *
 **/

using System;
using CoreJ2K.FaceOFFx.j2k;
using CoreJ2K.FaceOFFx.j2k.image;
using CoreJ2K.FaceOFFx.j2k.image.input;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace CoreJ2K.FaceOFFx.ImageSharp
{
    internal class ImgReaderImageSharp : ImgReader
    {
        /// <summary>DC offset value used when reading image</summary>
        private const int DC_OFFSET = 128;
        
        /// <summary>Buffer for the components of each pixel(in the current block)</summary>
        private int[][] barr;
	    
        /// <summary>Data block used only to store coordinates of the buffered blocks</summary>
        private readonly DataBlkInt dbi = new DataBlkInt();
        
        /// <summary>Temporary DataBlkInt object (needed when encoder uses floating-point
        /// filters). This avoids allocating new DataBlk at each time.</summary>
        private DataBlkInt intBlk;

        private readonly Image image;
        
        public ImgReaderImageSharp(Image<Rgb24> image)
        {
            this.image = image;
            w = image.Width;
            h = image.Height;
            nc = 3;
        }

        public override void Close()
        {
            // The caller owns the ImageSharp image; release only our sample buffers.
            barr = null;
        }

        /// <summary>
        /// Returns the number of bits corresponding to the nominal range of the
        /// data in the specified component. This is the value rb (range bits) that
        /// was specified in the constructor, which normally is 8 for non bi-level
        /// data, and 1 for bi-level data.
        /// 
        /// If this number is <i>b</i> then the nominal range is between
        /// -2^(b-1) and 2^(b-1)-1, since unsigned data is level shifted to have a
        /// nominal average of 0.
        /// </summary>
        /// <param name="compIndex">The index of the component.</param>
        /// <returns>The number of bits corresponding to the nominal range of the
        /// data.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Component index outside range</exception>
        public override int getNomRangeBits(int compIndex)
        {
            // Check component index
            if (compIndex < 0 || compIndex >= this.nc)
                throw new ArgumentOutOfRangeException(nameof(compIndex) + " is out of range");
			
            // ImageSharp typically uses 8-bit components
            return 8;
        }
        
        /// <summary>
        /// Returns the position of the fixed point in the specified component
        /// (i.e. the number of fractional bits), which is always 0 for this
        /// ImgReader.
        /// </summary>
        /// <param name="compIndex">The index of the component.</param>
        /// <returns> The position of the fixed-point (i.e. the number of fractional
        /// bits). Always 0 for this ImgReader.</returns>
        public override int GetFixedPoint(int compIndex)
        {
            // Check component index
            if (compIndex < 0 || compIndex >= nc)
                throw new ArgumentOutOfRangeException(nameof(compIndex) + " is out of range");
            return 0;
        }

        /// <summary>
        /// Returns, in the blk argument, the block of image data containing the
        /// specified rectangular area, in the specified component. The data is
        /// returned, as a reference to the internal data, if any, instead of as a
        /// copy, therefore the returned data should not be modified.
        /// 
        /// After being read the coefficients are level shifted by subtracting
        /// 2^(nominal bit range - 1)
        /// 
        /// The rectangular area to return is specified by the 'ulx', 'uly', 'w'
        /// and 'h' members of the 'blk' argument, relative to the current
        /// tile. These members are not modified by this method. The 'offset' and
        /// 'scanw' of the returned data can be arbitrary. See the 'DataBlk' class.
        /// 
        /// If the data array in <tt>blk</tt> is <tt>null</tt>, then a new one
        /// is created if necessary. The implementation of this interface may
        /// choose to return the same array or a new one, depending on what is more
        /// efficient. Therefore, the data array in <tt>blk</tt> prior to the
        /// method call should not be considered to contain the returned data, a
        /// new array may have been created. Instead, get the array from
        /// <tt>blk</tt> after the method has returned.
        /// 
        /// The returned data always has its 'progressive' attribute unset
        /// (i.e. false).
        /// 
        /// When an I/O exception is encountered the JJ2KExceptionHandler is
        /// used. The exception is passed to its handleException method. The action
        /// that is taken depends on the action that has been registered in
        /// JJ2KExceptionHandler. See JJ2KExceptionHandler for details.
        /// 
        /// This method implements buffering for the 3 components: When the
        /// first one is asked, all the 3 components are read and stored until they
        /// are needed.
        /// </summary>
        /// <param name="blk">Its coordinates and dimensions specify the area to
        /// return. Some fields in this object are modified to return the data.</param>
        /// <param name="compIndex">The index of the component from which to get the data. Only 0,
        /// 1 and 3 are valid.</param>
        /// <returns> The requested DataBlk</returns>
        /// <seealso cref="GetCompData" />
        /// <seealso cref="JJ2KExceptionHandler" />
        public override DataBlk GetInternCompData(DataBlk blk, int compIndex)
        {
            // Check component index
            if (compIndex < 0 || compIndex >= nc)
            {
                throw new ArgumentOutOfRangeException(nameof(compIndex) + " is out of range");
            }

            // Check type of block provided as an argument
            if (blk.DataType != DataBlk.TYPE_INT)
            {
                if (intBlk == null)
                {
                    intBlk = new DataBlkInt(blk.ulx, blk.uly, blk.w, blk.h);
                }
                else
                {
                    intBlk.ulx = blk.ulx;
                    intBlk.uly = blk.uly;
                    intBlk.w = blk.w;
                    intBlk.h = blk.h;
                }
                blk = intBlk;
            }

            // If asking a component for the first time for this block, read all the components
            if ((barr == null) || (dbi.ulx > blk.ulx) || (dbi.uly > blk.uly) 
                || (dbi.ulx + dbi.w < blk.ulx + blk.w) || (dbi.uly + dbi.h < blk.uly + blk.h))
            {
                barr = new int[nc][];

                // Reset data arrays if necessary
                for (var c = 0; c < nc; c++)
                {
                    if (barr[c] == null || barr[c].Length < blk.w * blk.h)
                    {
                        barr[c] = new int[blk.w * blk.h];
                    }
                }

                // set attributes of the DataBlk used for buffering
                dbi.ulx = blk.ulx;
                dbi.uly = blk.uly;
                dbi.w = blk.w;
                dbi.h = blk.h;

                // Read pixel data based on the image type
                ReadPixelData(blk);

                // Set buffer attributes
                blk.Data = barr[compIndex];
                blk.offset = 0;
                blk.scanw = blk.w;
            }
            else
            {
                //Asking for the 2nd or 3rd (or 4th) block component
                blk.Data = barr[compIndex];
                blk.offset = (blk.ulx - dbi.ulx) * dbi.w + blk.ulx - dbi.ulx;
                blk.scanw = dbi.scanw;
            }

            // Turn off the progressive attribute
            blk.progressive = false;
            return blk;
        }

        private void ReadPixelData(DataBlk blk) => ReadRgbPixels(blk);

        private void ReadRgbPixels(DataBlk blk)
        {
            var rgbImage = (Image<Rgb24>)image;
            var red = barr[0];
            var green = barr[1];
            var blue = barr[2];

            rgbImage.ProcessPixelRows(accessor =>
            {
                for (var y = blk.uly; y < blk.uly + blk.h; y++)
                {
                    if (y >= accessor.Height) break;
                    
                    var pixelRow = accessor.GetRowSpan(y);
                    for (var x = blk.ulx; x < blk.ulx + blk.w; x++)
                    {
                        if (x >= accessor.Width) break;
                        
                        var arrayIndex = (y - blk.uly) * blk.w + (x - blk.ulx);
                        var pixel = pixelRow[x];
                        red[arrayIndex] = pixel.R - DC_OFFSET;
                        green[arrayIndex] = pixel.G - DC_OFFSET;
                        blue[arrayIndex] = pixel.B - DC_OFFSET;
                    }
                }
            });
        }

        /// <summary>
        /// Returns, in the blk argument, a block of image data containing the
		/// specified rectangular area, in the specified component. The data is
		/// returned, as a copy of the internal data, therefore the returned data
		/// can be modified "in place".
		/// 
		/// After being read the coefficients are level shifted by subtracting
		/// 2^(nominal bit range - 1)
		/// 
		/// The rectangular area to return is specified by the 'ulx', 'uly', 'w'
		/// and 'h' members of the 'blk' argument, relative to the current
		/// tile. These members are not modified by this method. The 'offset' of
		/// the returned data is 0, and the 'scanw' is the same as the block's
		/// width. See the 'DataBlk' class.
		/// 
		/// If the data array in 'blk' is 'null', then a new one is created. If
		/// the data array is not 'null' then it is reused, and it must be large
		/// enough to contain the block's data. Otherwise an 'ArrayStoreException'
		/// or an 'IndexOutOfBoundsException' is thrown by the Java system.
		/// 
		/// The returned data has its 'progressive' attribute unset
		/// (i.e. false).
		/// 
		/// When an I/O exception is encountered the JJ2KExceptionHandler is
		/// used. The exception is passed to its handleException method. The action
		/// that is taken depends on the action that has been registered in
		/// JJ2KExceptionHandler. See JJ2KExceptionHandler for details.
		/// </summary>
		/// <param name="blk">Its coordinates and dimensions specify the area to
		/// return. If it contains a non-null data array, then it must have the
		/// correct dimensions. If it contains a null data array a new one is
		/// created. The fields in this object are modified to return the data.</param>
		/// <param name="c">The index of the component from which to get the data. Only
		/// 0,1 and 2 are valid.</param>
		/// <returns>The requested DataBlk</returns>
		/// <seealso cref="GetInternCompData" />
        public override DataBlk GetCompData(DataBlk blk, int c)
        {
            // Check type of block provided as an argument
            if (blk.DataType != DataBlk.TYPE_INT)
            {
                var tmp = new DataBlkInt(blk.ulx, blk.uly, blk.w, blk.h);
                blk = tmp;
            }

            var bakarr = (int[])blk.Data;
            // Save requested block size
            var ulx = blk.ulx;
            var uly = blk.uly;
            var width = blk.w;
            var height = blk.h;
            // Force internal data buffer to be different from external
            blk.Data = null;
            GetInternCompData(blk, c);
            // Copy the data
            if (bakarr == null)
            {
                bakarr = new int[width * height];
            }
            if (blk.offset == 0 && blk.scanw == width)
            {
                // Requested and returned block buffer are the same size
                Array.Copy((Array)blk.Data, 0, bakarr, 0, width * height);
            }
            else
            {
                // Requested and returned block are different
                for (var i = height - 1; i >= 0; i--)
                {
                    // copy line by line
                    Array.Copy((Array)blk.Data, blk.offset + i * blk.scanw, bakarr, i * width, width);
                }
            }
            blk.Data = bakarr;
            blk.offset = 0;
            blk.scanw = blk.w;
            return blk;
        }

        /// <summary>
        /// Returns true if the data read was originally signed in the specified
        /// component, false if not.
        /// </summary>
        /// <param name="compIndex">The index of the component, from 0 to N-1.</param>
        /// <returns> Data signededness.</returns>
        public override bool IsOrigSigned(int compIndex)
        {
            // Check component index
            if (compIndex < 0 || compIndex >= this.nc)
                throw new ArgumentOutOfRangeException(nameof(compIndex) + " is out of range");
            return false;
        }
        
        /// <summary>Returns a string of information about the object, more than 1 line long.</summary>
        /// <returns> A string of information about the object.</returns>
        public override string ToString()
        {
            return $"ImgReaderImageSharp: WxH = {w}x{h}, Components = {nc}";
        }
    }
}
