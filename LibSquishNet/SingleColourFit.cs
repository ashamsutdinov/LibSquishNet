using System.Numerics;

namespace LibSquishNet
{
    public class SingleColourFit :
        ColourFit
    {
        private readonly byte[] _mColour = new byte[3];

        private Vector3 _mStart;

        private Vector3 _mEnd;

        private byte _mIndex;

        private int _mError;

        private int _mBesterror;

        public SingleColourFit(ColourSet colours, SquishFlags flags)
            : base(colours, flags)
        {
            // grab the single colour
            var values = MColours.Points;
            _mColour[0] = (byte)ColourBlock.FloatToInt(255.0f * values[0].X, 255);
            _mColour[1] = (byte)ColourBlock.FloatToInt(255.0f * values[0].Y, 255);
            _mColour[2] = (byte)ColourBlock.FloatToInt(255.0f * values[0].Z, 255);

            // initialise the best error
            _mBesterror = int.MaxValue;
        }

        private void ComputeEndPoints(SingleColourLookup[][] lookups)
        {
            // check each index combination (endpoint or intermediate)
            _mError = int.MaxValue;
            for (var index = 0; index < 2; ++index)
            {
                // check the error for this codebook index
                var sources = new SourceBlock[3];
                var error = 0;
                for (var channel = 0; channel < 3; ++channel)
                {
                    // grab the lookup table and index for this channel
                    var lookup = lookups[channel];
                    int target = _mColour[channel];

                    // store a pointer to the source for this channel
                    sources[channel] = lookup[target].Sources[index];

                    // accumulate the error
                    int diff = sources[channel].Error;
                    error += diff * diff;
                }

                // keep it if the error is lower
                if (error >= _mError)
                    continue;

                _mStart = new Vector3(
                    sources[0].Start / 31.0f,
                    sources[1].Start / 63.0f,
                    sources[2].Start / 31.0f
                    );
                _mEnd = new Vector3(
                    sources[0].End / 31.0f,
                    sources[1].End / 63.0f,
                    sources[2].End / 31.0f
                    );
                _mIndex = (byte)(2 * index);
                _mError = error;
            }
        }

        protected override void Compress3(ref byte[] block, int offset)
        {
            // build the table of lookups
            var lookups = new[]
            {
                SingleColourLookupIns.Lookup53, 
                SingleColourLookupIns.Lookup63, 
                SingleColourLookupIns.Lookup53
            };

            // find the best end-points and index
            ComputeEndPoints(lookups);

            // build the block if we win
            if (_mError >= _mBesterror)
                return;

            // remap the indices
            var indices = new byte[16];
            MColours.RemapIndices(new[] { _mIndex }, indices);

            // save the block
            ColourBlock.WriteColourBlock3(_mStart, _mEnd, indices, ref block, offset);

            // save the error
            _mBesterror = _mError;
        }

        protected override void Compress4(ref byte[] block, int offset)
        {
            // build the table of lookups
            var lookups = new[]
            {
                SingleColourLookupIns.Lookup54, 
                SingleColourLookupIns.Lookup64, 
                SingleColourLookupIns.Lookup54
            };
        
            // find the best end-points and index
            ComputeEndPoints( lookups );
        
            // build the block if we win
            if (_mError >= _mBesterror)
                return;

            // remap the indices
            var indices = new byte[16];
            MColours.RemapIndices(new[] { _mIndex }, indices);
                
            // save the block
            ColourBlock.WriteColourBlock4( _mStart, _mEnd, indices, ref block, offset );

            // save the error
            _mBesterror = _mError;
        }
    }
}
