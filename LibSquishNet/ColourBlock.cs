﻿using System.Numerics;

namespace LibSquishNet
{
    public class ColourBlock
    {
        public static int FloatToInt(float a, int limit)
        {
            // use ANSI round-to-zero behaviour to get round-to-nearest
            var i = (int)(a + 0.5f);

            // clamp to the limit
            if (i < 0)
                i = 0;
            else if (i > limit)
                i = limit;

            // done
            return i;
        }

        private static int FloatTo565(Vector3 colour)
        {
            // get the components in the correct range
            var r = FloatToInt(31.0f * colour.X, 31);
            var g = FloatToInt(63.0f * colour.Y, 63);
            var b = FloatToInt(31.0f * colour.Z, 31);

            // pack into a single value
            return (r << 11) | (g << 5) | b;
        }

        private static void WriteColourBlock(int a, int b, byte[] indices, ref byte[] block, int offset)
        {
            // write the endpoints
            block[offset + 0] = (byte)(a & 0xff);
            block[offset + 1] = (byte)(a >> 8);
            block[offset + 2] = (byte)(b & 0xff);
            block[offset + 3] = (byte)(b >> 8);

            // write the indices
            for (var i = 0; i < 4; ++i)
            {
                block[offset + 4 + i] = (byte)(indices[4 * i + 0] | (indices[4 * i + 1] << 2) | (indices[4 * i + 2] << 4) | (indices[4 * i + 3] << 6));
            }
        }

        public static void WriteColourBlock3(Vector3 start, Vector3 end, byte[] indices, ref byte[] block, int offset)
        {
            // get the packed values
            var a = FloatTo565(start);
            var b = FloatTo565(end);

            // remap the indices
            var remapped = new byte[16];
            if (a <= b)
            {
                // use the indices directly
                for (var i = 0; i < 16; ++i)
                    remapped[i] = indices[i];
            }
            else
            {
                // swap a and b
                var t = a;
                a = b;
                b = t;
                for (var i = 0; i < 16; ++i)
                {
                    if (indices[i] == 0)
                        remapped[i] = 1;
                    else if (indices[i] == 1)
                        remapped[i] = 0;
                    else
                        remapped[i] = indices[i];
                }
            }

            // write the block
            WriteColourBlock(a, b, remapped, ref block, offset);
        }

        public static void WriteColourBlock4(Vector3 start, Vector3 end, byte[] indices, ref byte[] block, int offset)
        {
            // get the packed values
            var a = FloatTo565(start);
            var b = FloatTo565(end);

            // remap the indices
            var remapped = new byte[16];
            if (a < b)
            {
                // swap a and b
                var t = a;
                a = b;
                b = t;
                for (var i = 0; i < 16; ++i)
                    remapped[i] = (byte)((indices[i] ^ 0x1) & 0x3);
            }
            else if (a == b)
            {
                // use index 0
                for (var i = 0; i < 16; ++i)
                    remapped[i] = 0;
            }
            else
            {
                // use the indices directly
                for (var i = 0; i < 16; ++i)
                    remapped[i] = indices[i];
            }

            // write the block
            WriteColourBlock(a, b, remapped, ref block, offset);
        }

        private static int Unpack565(byte[] packed, int offset, byte[] colour, int colouroffset)
        {
            // build the packed value
            var value = (int)packed[offset + 0] | ((int)packed[offset + 1] << 8);

            // get the components in the stored range
            var red = (byte)((value >> 11) & 0x1f);
            var green = (byte)((value >> 5) & 0x3f);
            var blue = (byte)(value & 0x1f);

            // scale up to 8 bits
            colour[colouroffset + 0] = (byte)((red << 3) | (red >> 2));
            colour[colouroffset + 1] = (byte)((green << 2) | (green >> 4));
            colour[colouroffset + 2] = (byte)((blue << 3) | (blue >> 2));
            colour[colouroffset + 3] = 255;

            // return the value
            return value;
        }

        public static void DecompressColour(byte[] rgba, ref byte[] block, int offset, bool isDxt1)
        {
            // unpack the endpoints
            var codes = new byte[16];
            var a = Unpack565(block, offset, codes, 0);
            var b = Unpack565(block, offset + 2, codes, 4);

            // generate the midpoints
            for (var i = 0; i < 3; ++i)
            {
                int c = codes[i];
                int d = codes[4 + i];

                if (isDxt1 && a <= b)
                {
                    codes[8 + i] = (byte)((c + d) / 2);
                    codes[12 + i] = 0;
                }
                else
                {
                    codes[8 + i] = (byte)((2 * c + d) / 3);
                    codes[12 + i] = (byte)((c + 2 * d) / 3);
                }
            }

            // fill in alpha for the intermediate values
            codes[8 + 3] = 255;
            codes[12 + 3] = (byte)(isDxt1 && a <= b ? 0 : 255);

            // unpack the indices
            var indices = new byte[16];
            for (var i = 0; i < 4; ++i)
            {
                var ind = 4 * i;
                var packed = block[offset + 4 + i];

                indices[ind + 0] = (byte)(packed & 0x3);
                indices[ind + 1] = (byte)((packed >> 2) & 0x3);
                indices[ind + 2] = (byte)((packed >> 4) & 0x3);
                indices[ind + 3] = (byte)((packed >> 6) & 0x3);
            }

            // store out the colours
            for (var i = 0; i < 16; ++i)
            {
                var coffset = (byte)(4 * indices[i]);
                for (var j = 0; j < 4; ++j)
                {
                    rgba[4 * i + j] = codes[coffset + j];
                }
            }
        }
    }
}
