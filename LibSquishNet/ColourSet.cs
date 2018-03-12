﻿using System;
using System.Numerics;

namespace LibSquishNet
{
    public class ColourSet
    {
        private readonly int _mCount;
        private readonly Vector3[] _mPoints = new Vector3[16];
        private readonly float[] _mWeights = new float[16];
        private readonly int[] _mRemap = new int[16];
        private readonly bool _mTransparent;

        public int Count { get { return _mCount; } }
        public bool IsTransparent { get { return _mTransparent; } }
        public Vector3[] Points { get { return _mPoints; } }
        public float[] Weights { get { return _mWeights; } }

        public ColourSet(byte[] rgba, int mask, SquishFlags flags)
        {
            // check the compression mode for dxt1
            var isDxt1 = (flags & SquishFlags.KDxt1) != 0;
            var weightByAlpha = (flags & SquishFlags.KWeightColourByAlpha) != 0;

            // create the minimal set
            for (var i = 0; i < 16; ++i)
            {
                // check this pixel is enabled
                var bit = 1 << i;
                if ((mask & bit) == 0)
                {
                    _mRemap[i] = -1;
                    continue;
                }

                // check for transparent pixels when using dxt1
                if (isDxt1 && rgba[4 * i + 3] < 128)
                {
                    _mRemap[i] = -1;
                    _mTransparent = true;
                    continue;
                }

                // loop over previous points for a match
                for (var j = 0; ; ++j)
                {
                    // allocate a new point
                    if (j == i)
                    {
                        // normalise coordinates to [0,1]
                        var x = rgba[4 * i] / 255.0f;
                        var y = rgba[4 * i + 1] / 255.0f;
                        var z = rgba[4 * i + 2] / 255.0f;

                        // ensure there is always non-zero weight even for zero alpha
                        var w = (rgba[4 * i + 3] + 1) / 256.0f;

                        // add the point
                        _mPoints[_mCount] = new Vector3(x, y, z);
                        _mWeights[_mCount] = weightByAlpha ? w : 1.0f;
                        _mRemap[i] = _mCount;

                        // advance
                        ++_mCount;
                        break;
                    }

                    // check for a match
                    var oldbit = 1 << j;
                    var match = ((mask & oldbit) != 0)
                            && (rgba[4 * i] == rgba[4 * j])
                            && (rgba[4 * i + 1] == rgba[4 * j + 1])
                            && (rgba[4 * i + 2] == rgba[4 * j + 2])
                            && (rgba[4 * j + 3] >= 128 || !isDxt1);
                    if (match)
                    {
                        // get the index of the match
                        var index = _mRemap[j];

                        // ensure there is always non-zero weight even for zero alpha
                        var w = (rgba[4 * i + 3] + 1) / 256.0f;

                        // map to this point and increase the weight
                        _mWeights[index] += weightByAlpha ? w : 1.0f;
                        _mRemap[i] = index;
                        break;
                    }
                }
            }

            // square root the weights
            for (var i = 0; i < _mCount; ++i)
            {
                _mWeights[i] = (float)Math.Sqrt(_mWeights[i]);
            }
        }

        public void RemapIndices(byte[] source, byte[] target)
        {
            for (var i = 0; i < 16; ++i)
            {
                var j = _mRemap[i];
                if (j == -1)
                {
                    target[i] = 3;
                }
                else
                {
                    target[i] = source[j];
                }
            }
        }
    }
}
