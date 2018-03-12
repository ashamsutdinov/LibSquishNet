﻿using System;

namespace LibSquishNet
{
    [Flags]
    public enum SquishFlags
    {
        //! Use DXT1 compression.
        KDxt1 = 1,

        //! Use DXT3 compression.
        KDxt3 = 2,

        //! Use DXT5 compression.
        KDxt5 = 4,

        //! Use a very slow but very high quality colour compressor.
        KColourIterativeClusterFit = 256,

        //! Use a slow but high quality colour compressor (the default).
        KColourClusterFit = 8,

        //! Use a fast but low quality colour compressor.
        KColourRangeFit = 16,

        //! Weight the colour by alpha during cluster fit (disabled by default).
        KWeightColourByAlpha = 128
    };

    public static class Squish
    {
        static SquishFlags FixFlags(SquishFlags flags)
        {
            // grab the flag bits
            var method = flags & (SquishFlags.KDxt1 | SquishFlags.KDxt3 | SquishFlags.KDxt5);
            var fit = flags & (SquishFlags.KColourIterativeClusterFit | SquishFlags.KColourClusterFit | SquishFlags.KColourRangeFit);
            var extra = flags & SquishFlags.KWeightColourByAlpha;

            // set defaults
            if (method != SquishFlags.KDxt3 && method != SquishFlags.KDxt5) { method = SquishFlags.KDxt1; }
            if (fit != SquishFlags.KColourRangeFit && fit != SquishFlags.KColourIterativeClusterFit) { fit = SquishFlags.KColourClusterFit; }

            // done
            return method | fit | extra;
        }

        public static int GetStorageRequirements(int width, int height, SquishFlags flags)
        {
            // fix any bad flags
            flags = FixFlags(flags);

            // compute the storage requirements
            int blockcount = ((width + 3) / 4) * ((height + 3) / 4);
            int blocksize = ((flags & SquishFlags.KDxt1) != 0) ? 8 : 16;
            return blockcount * blocksize;
        }

        public static void DecompressImage(byte[] rgba, int width, int height, ref byte[] blocks, SquishFlags flags)
        {
            // fix any bad flags
            flags = FixFlags(flags);

            // initialise the block input
            int sourceBlock = 0;
            int bytesPerBlock = ((flags & SquishFlags.KDxt1) != 0) ? 8 : 16;

            // loop over blocks
            for (int y = 0; y < height; y += 4)
            {
                for (int x = 0; x < width; x += 4)
                {
                    // decompress the block
                    byte[] targetRgba = new byte[4 * 16];
                    Decompress(targetRgba, ref blocks, sourceBlock, flags);

                    // write the decompressed pixels to the correct image locations
                    int sourcePixel = 0;
                    for (int py = 0; py < 4; ++py)
                    {
                        for (int px = 0; px < 4; ++px)
                        {
                            // get the target location
                            int sx = x + px;
                            int sy = y + py;
                            if (sx < width && sy < height)
                            {
                                int targetPixel = 4 * (width * sy + sx);

                                // copy the rgba value
                                for (int i = 0; i < 4; ++i)
                                {
                                    rgba[targetPixel] = targetRgba[sourcePixel];

                                    targetPixel++;
                                    sourcePixel++;
                                }
                            }
                            else
                            {
                                // skip this pixel as its outside the image
                                sourcePixel += 4;
                            }
                        }
                    }

                    // advance
                    sourceBlock += bytesPerBlock;
                }
            }
        }

        static void Decompress(byte[] rgba, ref byte[] block, int offset, SquishFlags flags)
        {
            // fix any bad flags
            flags = FixFlags(flags);

            // get the block locations
            int colourBlock = offset;
            int alphaBlock = offset;
            if ((flags & (SquishFlags.KDxt3 | SquishFlags.KDxt5)) != 0) { colourBlock += 8; }

            // decompress colour
            ColourBlock.DecompressColour(rgba, ref block, colourBlock, (flags & SquishFlags.KDxt1) != 0);

            // decompress alpha separately if necessary
            if ((flags & SquishFlags.KDxt3) != 0)
            {
                throw new NotImplementedException("Squish.DecompressAlphaDxt3");
                //DecompressAlphaDxt3(rgba, alphaBlock);
            }
            else if ((flags & SquishFlags.KDxt5) != 0)
            {
                DecompressAlphaDxt5(rgba, ref block, alphaBlock);
            }
        }

        public static void CompressImage(byte[] rgba, int width, int height, ref byte[] blocks, SquishFlags flags)
        {
            // fix any bad flags
            flags = FixFlags(flags);

            // initialise the block output
            int targetBlock = 0;
            int bytesPerBlock = (flags.HasFlag(SquishFlags.KDxt1) ? 8 : 16);

            // loop over blocks
            for (int y = 0; y < height; y += 4)
            {
                for (int x = 0; x < width; x += 4)
                {
                    // build the 4x4 block of pixels
                    byte[] sourceRgba = new byte[16 * 4];
                    byte targetPixel = 0;
                    int mask = 0;

                    for (int py = 0; py < 4; ++py)
                    {
                        for (int px = 0; px < 4; ++px)
                        {
                            // get the source pixel in the image
                            int sx = x + px;
                            int sy = y + py;

                            // enable if we're in the image
                            if (sx < width && sy < height)
                            {
                                // copy the rgba value
                                for (int i = 0; i < 4; ++i)
                                {
                                    sourceRgba[targetPixel] = rgba[i + 4 * (width * sy + sx)];
                                    targetPixel++;
                                }

                                // enable this pixel
                                mask |= (1 << (4 * py + px));
                            }
                            else
                            {
                                // skip this pixel as its outside the image
                                targetPixel += 4;
                            }
                        }
                    }

                    // compress it into the output
                    CompressMasked(sourceRgba, mask, ref blocks, targetBlock, flags, null);

                    // advance
                    targetBlock += bytesPerBlock;
                }
            }
        }

        static void CompressMasked(byte[] rgba, int mask, ref byte[] block, int offset, SquishFlags flags, float? metric)
        {
            // fix any bad flags
            flags = FixFlags(flags);

            // get the block locations
            int colourBlock = offset;
            int alphaBlock = offset;
            if ((flags & (SquishFlags.KDxt3 | SquishFlags.KDxt5)) != 0) { colourBlock += 8; }

            // create the minimal point set
            ColourSet colours = new ColourSet(rgba, mask, flags);

            // check the compression type and compress colour
            if (colours.Count == 1)
            {
                // always do a single colour fit
                SingleColourFit fit = new SingleColourFit(colours, flags);
                fit.Compress(ref block, colourBlock);
            }
            else if ((flags & SquishFlags.KColourRangeFit) != 0 || colours.Count == 0)
            {
                // do a range fit
                RangeFit fit = new RangeFit(colours, flags, metric);
                fit.Compress(ref block, colourBlock);
            }
            else
            {
                // default to a cluster fit (could be iterative or not)
                ClusterFit fit = new ClusterFit(colours, flags, metric);
                fit.Compress(ref block, colourBlock);
            }

            // compress alpha separately if necessary
            if ((flags & SquishFlags.KDxt3) != 0)
            {
                CompressAlphaDxt3(rgba, mask, ref block, alphaBlock);
            }
            else if ((flags & SquishFlags.KDxt5) != 0)
            {
                CompressAlphaDxt5(rgba, mask, ref block, alphaBlock);
            }
        }

        static void CompressAlphaDxt3(byte[] rgba, int mask, ref byte[] block, int offset)
        {
            // quantise and pack the alpha values pairwise
            for (int i = 0; i < 8; ++i)
            {
                // quantise down to 4 bits
                float alpha1 = (float)rgba[8 * i + 3] * (15.0f / 255.0f);
                float alpha2 = (float)rgba[8 * i + 7] * (15.0f / 255.0f);
                int quant1 = ColourBlock.FloatToInt(alpha1, 15);
                int quant2 = ColourBlock.FloatToInt(alpha2, 15);

                // set alpha to zero where masked
                int bit1 = 1 << (2 * i);
                int bit2 = 1 << (2 * i + 1);
                if ((mask & bit1) == 0)
                    quant1 = 0;
                if ((mask & bit2) == 0)
                    quant2 = 0;

                // pack into the byte
                block[i + offset] = (byte)(quant1 | (quant2 << 4));
            }
        }

        static void FixRange(int min, int max, int steps)
        {
            if (max - min < steps)
                max = Math.Min(min + steps, 255);
            if (max - min < steps)
                min = Math.Max(0, max - steps);
        }

        static int FitCodes(byte[] rgba, int mask, byte[] codes, byte[] indices)
        {
            // fit each alpha value to the codebook
            int err = 0;
            for (int i = 0; i < 16; ++i)
            {
                // check this pixel is valid
                int bit = 1 << i;
                if ((mask & bit) == 0)
                {
                    // use the first code
                    indices[i] = 0;
                    continue;
                }

                // find the least error and corresponding index
                int value = rgba[4 * i + 3];
                int least = int.MaxValue;
                int index = 0;
                for (int j = 0; j < 8; ++j)
                {
                    // get the squared error from this code
                    int dist = (int)value - (int)codes[j];
                    dist *= dist;

                    // compare with the best so far
                    if (dist < least)
                    {
                        least = dist;
                        index = j;
                    }
                }

                // save this index and accumulate the error
                indices[i] = (byte)index;
                err += least;
            }

            // return the total error
            return err;
        }

        static void WriteAlphaBlock(int alpha0, int alpha1, byte[] indices, ref byte[] block, int offset)
        {
            // write the first two bytes
            block[offset + 0] = (byte)alpha0;
            block[offset + 1] = (byte)alpha1;

            // pack the indices with 3 bits each
            int dest = offset + 2;
            int src = 0;
            for (int i = 0; i < 2; ++i)
            {
                // pack 8 3-bit values
                int value = 0;
                for (int j = 0; j < 8; ++j)
                {
                    int index = indices[src];
                    value |= (index << 3 * j);
                    src++;
                }

                // store in 3 bytes
                for (int j = 0; j < 3; ++j)
                {
                    int b = (value >> 8 * j) & 0xff;
                    block[dest] = (byte)b;
                    dest++;
                }
            }
        }

        static void WriteAlphaBlock5(int alpha0, int alpha1, byte[] indices, ref byte[] block, int offset)
        {
            // check the relative values of the endpoints
            if (alpha0 > alpha1)
            {
                // swap the indices
                byte[] swapped = new byte[16];
                for (int i = 0; i < 16; ++i)
                {
                    byte index = indices[i];
                    if (index == 0)
                        swapped[i] = 1;
                    else if (index == 1)
                        swapped[i] = 0;
                    else if (index <= 5)
                        swapped[i] = (byte)(7 - index);
                    else
                        swapped[i] = index;
                }

                // write the block
                WriteAlphaBlock(alpha1, alpha0, swapped, ref block, offset);
            }
            else
            {
                // write the block
                WriteAlphaBlock(alpha0, alpha1, indices, ref block, offset);
            }
        }

        static void WriteAlphaBlock7(int alpha0, int alpha1, byte[] indices, ref byte[] block, int offset)
        {
            // check the relative values of the endpoints
            if (alpha0 < alpha1)
            {
                // swap the indices
                byte[] swapped = new byte[16];
                for (int i = 0; i < 16; ++i)
                {
                    byte index = indices[i];
                    if (index == 0)
                        swapped[i] = 1;
                    else if (index == 1)
                        swapped[i] = 0;
                    else
                        swapped[i] = (byte)(9 - index);
                }

                // write the block
                WriteAlphaBlock(alpha1, alpha0, swapped, ref block, offset);
            }
            else
            {
                // write the block
                WriteAlphaBlock(alpha0, alpha1, indices, ref block, offset);
            }
        }

        static void CompressAlphaDxt5( byte[] rgba, int mask, ref byte[] block, int offset )
        {
            // get the range for 5-alpha and 7-alpha interpolation
            int min5 = 255;
            int max5 = 0;
            int min7 = 255;
            int max7 = 0;
            for( int i = 0; i < 16; ++i )
            {
                    // check this pixel is valid
                    int bit = 1 << i;
                    if( ( mask & bit ) == 0 )
                            continue;

                    // incorporate into the min/max
                    int value = rgba[4*i + 3];
                    if( value < min7 )
                            min7 = value;
                    if( value > max7 )
                            max7 = value;
                    if( value != 0 && value < min5 )
                            min5 = value;
                    if( value != 255 && value > max5 )
                            max5 = value;
            }
        
            // handle the case that no valid range was found
            if( min5 > max5 )
                    min5 = max5;
            if( min7 > max7 )
                    min7 = max7;
                
            // fix the range to be the minimum in each case
            FixRange( min5, max5, 5 );
            FixRange( min7, max7, 7 );
        
            // set up the 5-alpha code book
            byte[] codes5 = new byte[8];
            codes5[0] = ( byte )min5;
            codes5[1] = ( byte )max5;
            for( int i = 1; i < 5; ++i )
                    codes5[1 + i] = ( byte )( ( ( 5 - i )*min5 + i*max5 )/5 );
            codes5[6] = 0;
            codes5[7] = 255;
        
            // set up the 7-alpha code book
            byte[] codes7 = new byte[8];
            codes7[0] = ( byte )min7;
            codes7[1] = ( byte )max7;
            for( int i = 1; i < 7; ++i )
                    codes7[1 + i] = ( byte )( ( ( 7 - i )*min7 + i*max7 )/7 );
                
            // fit the data to both code books
            byte[] indices5 = new byte[16];
            byte[] indices7 = new byte[16];
            int err5 = FitCodes( rgba, mask, codes5, indices5 );
            int err7 = FitCodes( rgba, mask, codes7, indices7 );
        
            // save the block with least error
            if( err5 <= err7 )
                    WriteAlphaBlock5( min5, max5, indices5, ref block, offset );
            else
                    WriteAlphaBlock7( min7, max7, indices7, ref block, offset );
        }

        static void DecompressAlphaDxt5(byte[] rgba, ref byte[] block, int offset)
        {
            // get the two alpha values
            int alpha0 = block[offset + 0];
            int alpha1 = block[offset + 1];

            // compare the values to build the codebook
            byte[] codes = new byte[8];
            codes[0] = (byte)alpha0;
            codes[1] = (byte)alpha1;
            if (alpha0 <= alpha1)
            {
                // use 5-alpha codebook
                for (int i = 1; i < 5; ++i)
                {
                    codes[1 + i] = (byte)(((5 - i) * alpha0 + i * alpha1) / 5);
                }
                codes[6] = 0;
                codes[7] = 255;
            }
            else
            {
                // use 7-alpha codebook
                for (int i = 1; i < 7; ++i)
                {
                    codes[1 + i] = (byte)(((7 - i) * alpha0 + i * alpha1) / 7);
                }
            }

            // decode the indices
            byte[] indices = new byte[16];
            int src = offset + 2;
            int dest = 0;
            for (int i = 0; i < 2; ++i)
            {
                // grab 3 bytes
                int value = 0;
                for (int j = 0; j < 3; ++j)
                {
                    int b = block[src++];
                    value |= (b << 8 * j);
                }

                // unpack 8 3-bit values from it
                for (int j = 0; j < 8; ++j)
                {
                    int index = (value >> 3 * j) & 0x7;
                    indices[dest++] = (byte)index;
                }
            }

            // write out the indexed codebook values
            for (int i = 0; i < 16; ++i)
            {
                rgba[4 * i + 3] = codes[indices[i]];
            }
        }
    }
}
