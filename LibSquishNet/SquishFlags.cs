using System;

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
}