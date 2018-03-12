using System;

namespace LibSquishNet
{
    public static class Squish
    {
        private static SquishFlags FixFlags(SquishFlags flags)
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
            var blockcount = (width + 3) / 4 * ((height + 3) / 4);
            var blocksize = (flags & SquishFlags.KDxt1) != 0 ? 8 : 16;
            return blockcount * blocksize;
        }

        public static void DecompressImage(byte[] rgba, int width, int height, ref byte[] blocks, SquishFlags flags)
        {
            // fix any bad flags
            flags = FixFlags(flags);

            // initialise the block input
            var sourceBlock = 0;
            var bytesPerBlock = (flags & SquishFlags.KDxt1) != 0 ? 8 : 16;

            // loop over blocks
            // decompress the block
            var targetRgba = new byte[4 * 16];
            var zeroArr = new byte[4 * 16];

            for (var y = 0; y < height; y += 4)
            {
                for (var x = 0; x < width; x += 4)
                {
                    Array.Copy(zeroArr, targetRgba, 4 * 16);
                    Decompress(targetRgba, ref blocks, sourceBlock, flags);

                    // write the decompressed pixels to the correct image locations
                    var sourcePixel = 0;
                    for (var py = 0; py < 4; ++py)
                    {
                        for (var px = 0; px < 4; ++px)
                        {
                            // get the target location
                            var sx = x + px;
                            var sy = y + py;
                            if (sx < width && sy < height)
                            {
                                var targetPixel = 4 * (width * sy + sx);

                                // copy the rgba value
                                for (var i = 0; i < 4; ++i)
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

        private static void Decompress(byte[] rgba, ref byte[] block, int offset, SquishFlags flags)
        {
            // fix any bad flags
            flags = FixFlags(flags);

            // get the block locations
            var colourBlock = offset;
            var alphaBlock = offset;
            if ((flags & (SquishFlags.KDxt3 | SquishFlags.KDxt5)) != 0) { colourBlock += 8; }

            // decompress colour
            ColourBlock.DecompressColour(rgba, ref block, colourBlock, (flags & SquishFlags.KDxt1) != 0);

            // decompress alpha separately if necessary
            if ((flags & SquishFlags.KDxt3) != 0)
            {
                throw new NotImplementedException("Squish.DecompressAlphaDxt3");
                //DecompressAlphaDxt3(rgba, alphaBlock);
            }
            if ((flags & SquishFlags.KDxt5) != 0)
            {
                DecompressAlphaDxt5(rgba, ref block, alphaBlock);
            }
        }

        public static void CompressImage(byte[] rgba, int width, int height, ref byte[] blocks, SquishFlags flags)
        {
            // fix any bad flags
            flags = FixFlags(flags);

            // initialise the block output
            var targetBlock = 0;
            var bytesPerBlock = flags.HasFlag(SquishFlags.KDxt1) ? 8 : 16;

            // loop over blocks
            for (var y = 0; y < height; y += 4)
            {
                for (var x = 0; x < width; x += 4)
                {
                    // build the 4x4 block of pixels
                    var sourceRgba = new byte[16 * 4];
                    byte targetPixel = 0;
                    var mask = 0;

                    for (var py = 0; py < 4; ++py)
                    {
                        for (var px = 0; px < 4; ++px)
                        {
                            // get the source pixel in the image
                            var sx = x + px;
                            var sy = y + py;

                            // enable if we're in the image
                            if (sx < width && sy < height)
                            {
                                // copy the rgba value
                                for (var i = 0; i < 4; ++i)
                                {
                                    sourceRgba[targetPixel] = rgba[i + 4 * (width * sy + sx)];
                                    targetPixel++;
                                }

                                // enable this pixel
                                mask |= 1 << (4 * py + px);
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

        private static void CompressMasked(byte[] rgba, int mask, ref byte[] block, int offset, SquishFlags flags, float? metric)
        {
            // fix any bad flags
            flags = FixFlags(flags);

            // get the block locations
            var colourBlock = offset;
            var alphaBlock = offset;
            if ((flags & (SquishFlags.KDxt3 | SquishFlags.KDxt5)) != 0) { colourBlock += 8; }

            // create the minimal point set
            var colours = new ColourSet(rgba, mask, flags);

            // check the compression type and compress colour
            if (colours.Count == 1)
            {
                // always do a single colour fit
                var fit = new SingleColourFit(colours, flags);
                fit.Compress(ref block, colourBlock);
            }
            else if ((flags & SquishFlags.KColourRangeFit) != 0 || colours.Count == 0)
            {
                // do a range fit
                var fit = new RangeFit(colours, flags, metric);
                fit.Compress(ref block, colourBlock);
            }
            else
            {
                // default to a cluster fit (could be iterative or not)
                var fit = new ClusterFit(colours, flags, metric);
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

        private static void CompressAlphaDxt3(byte[] rgba, int mask, ref byte[] block, int offset)
        {
            // quantise and pack the alpha values pairwise
            for (var i = 0; i < 8; ++i)
            {
                // quantise down to 4 bits
                var alpha1 = rgba[8 * i + 3] * (15.0f / 255.0f);
                var alpha2 = rgba[8 * i + 7] * (15.0f / 255.0f);
                var quant1 = ColourBlock.FloatToInt(alpha1, 15);
                var quant2 = ColourBlock.FloatToInt(alpha2, 15);

                // set alpha to zero where masked
                var bit1 = 1 << (2 * i);
                var bit2 = 1 << (2 * i + 1);
                if ((mask & bit1) == 0)
                    quant1 = 0;
                if ((mask & bit2) == 0)
                    quant2 = 0;

                // pack into the byte
                block[i + offset] = (byte)(quant1 | (quant2 << 4));
            }
        }

        private static void FixRange(int min, int max, int steps)
        {
            if (max - min < steps)
                max = Math.Min(min + steps, 255);
            if (max - min < steps)
                min = Math.Max(0, max - steps);
        }

        private static int FitCodes(byte[] rgba, int mask, byte[] codes, byte[] indices)
        {
            // fit each alpha value to the codebook
            var err = 0;
            for (var i = 0; i < 16; ++i)
            {
                // check this pixel is valid
                var bit = 1 << i;
                if ((mask & bit) == 0)
                {
                    // use the first code
                    indices[i] = 0;
                    continue;
                }

                // find the least error and corresponding index
                int value = rgba[4 * i + 3];
                var least = int.MaxValue;
                var index = 0;
                for (var j = 0; j < 8; ++j)
                {
                    // get the squared error from this code
                    var dist = value - codes[j];
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

        private static void WriteAlphaBlock(int alpha0, int alpha1, byte[] indices, ref byte[] block, int offset)
        {
            // write the first two bytes
            block[offset + 0] = (byte)alpha0;
            block[offset + 1] = (byte)alpha1;

            // pack the indices with 3 bits each
            var dest = offset + 2;
            var src = 0;
            for (var i = 0; i < 2; ++i)
            {
                // pack 8 3-bit values
                var value = 0;
                for (var j = 0; j < 8; ++j)
                {
                    int index = indices[src];
                    value |= index << 3 * j;
                    src++;
                }

                // store in 3 bytes
                for (var j = 0; j < 3; ++j)
                {
                    var b = (value >> 8 * j) & 0xff;
                    block[dest] = (byte)b;
                    dest++;
                }
            }
        }

        private static void WriteAlphaBlock5(int alpha0, int alpha1, byte[] indices, ref byte[] block, int offset)
        {
            // check the relative values of the endpoints
            if (alpha0 > alpha1)
            {
                // swap the indices
                var swapped = new byte[16];
                for (var i = 0; i < 16; ++i)
                {
                    var index = indices[i];
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

        private static void WriteAlphaBlock7(int alpha0, int alpha1, byte[] indices, ref byte[] block, int offset)
        {
            // check the relative values of the endpoints
            if (alpha0 < alpha1)
            {
                // swap the indices
                var swapped = new byte[16];
                for (var i = 0; i < 16; ++i)
                {
                    var index = indices[i];
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

        private static void CompressAlphaDxt5(byte[] rgba, int mask, ref byte[] block, int offset)
        {
            // get the range for 5-alpha and 7-alpha interpolation
            var min5 = 255;
            var max5 = 0;
            var min7 = 255;
            var max7 = 0;
            for (var i = 0; i < 16; ++i)
            {
                // check this pixel is valid
                var bit = 1 << i;
                if ((mask & bit) == 0)
                    continue;

                // incorporate into the min/max
                int value = rgba[4 * i + 3];
                if (value < min7)
                    min7 = value;
                if (value > max7)
                    max7 = value;
                if (value != 0 && value < min5)
                    min5 = value;
                if (value != 255 && value > max5)
                    max5 = value;
            }

            // handle the case that no valid range was found
            if (min5 > max5)
                min5 = max5;
            if (min7 > max7)
                min7 = max7;

            // fix the range to be the minimum in each case
            FixRange(min5, max5, 5);
            FixRange(min7, max7, 7);

            // set up the 5-alpha code book
            var codes5 = new byte[8];
            codes5[0] = (byte)min5;
            codes5[1] = (byte)max5;
            for (var i = 1; i < 5; ++i)
                codes5[1 + i] = (byte)(((5 - i) * min5 + i * max5) / 5);
            codes5[6] = 0;
            codes5[7] = 255;

            // set up the 7-alpha code book
            var codes7 = new byte[8];
            codes7[0] = (byte)min7;
            codes7[1] = (byte)max7;
            for (var i = 1; i < 7; ++i)
                codes7[1 + i] = (byte)(((7 - i) * min7 + i * max7) / 7);

            // fit the data to both code books
            var indices5 = new byte[16];
            var indices7 = new byte[16];
            var err5 = FitCodes(rgba, mask, codes5, indices5);
            var err7 = FitCodes(rgba, mask, codes7, indices7);

            // save the block with least error
            if (err5 <= err7)
                WriteAlphaBlock5(min5, max5, indices5, ref block, offset);
            else
                WriteAlphaBlock7(min7, max7, indices7, ref block, offset);
        }

        private static void DecompressAlphaDxt5(byte[] rgba, ref byte[] block, int offset)
        {
            // get the two alpha values
            int alpha0 = block[offset + 0];
            int alpha1 = block[offset + 1];

            // compare the values to build the codebook
            var codes = new byte[8];
            codes[0] = (byte)alpha0;
            codes[1] = (byte)alpha1;
            if (alpha0 <= alpha1)
            {
                // use 5-alpha codebook
                for (var i = 1; i < 5; ++i)
                {
                    codes[1 + i] = (byte)(((5 - i) * alpha0 + i * alpha1) / 5);
                }
                codes[6] = 0;
                codes[7] = 255;
            }
            else
            {
                // use 7-alpha codebook
                for (var i = 1; i < 7; ++i)
                {
                    codes[1 + i] = (byte)(((7 - i) * alpha0 + i * alpha1) / 7);
                }
            }

            // decode the indices
            var indices = new byte[16];
            var src = offset + 2;
            var dest = 0;
            for (var i = 0; i < 2; ++i)
            {
                // grab 3 bytes
                var value = 0;
                for (var j = 0; j < 3; ++j)
                {
                    int b = block[src++];
                    value |= b << 8 * j;
                }

                // unpack 8 3-bit values from it
                for (var j = 0; j < 8; ++j)
                {
                    var index = (value >> 3 * j) & 0x7;
                    indices[dest++] = (byte)index;
                }
            }

            // write out the indexed codebook values
            for (var i = 0; i < 16; ++i)
            {
                rgba[4 * i + 3] = codes[indices[i]];
            }
        }
    }
}
