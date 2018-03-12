using System;
using System.Numerics;

namespace LibSquishNet
{
    public class RangeFit :
        ColourFit
    {
        private readonly Vector3 _mMetric;

        private readonly Vector3 _mStart;

        private readonly Vector3 _mEnd;
        private float _mBesterror;


        public RangeFit(ColourSet colours, SquishFlags flags, float? metric)
            : base(colours, flags)
        {
            // initialise the metric (old perceptual = 0.2126f, 0.7152f, 0.0722f)
            if (metric != null)
            {
                //m_metric = new Vector3( metric[0], metric[1], metric[2] );
            }
            else
            {
                _mMetric = new Vector3(1.0f);
            }

            // initialise the best error
            _mBesterror = float.MaxValue;

            // cache some values
            var count = MColours.Count;
            var values = MColours.Points;
            var weights = MColours.Weights;

            // get the covariance matrix
            var covariance = Sym3X3.ComputeWeightedCovariance(count, values, weights);

            // compute the principle component
            var principle = Sym3X3.ComputePrincipleComponent(covariance);

            // get the min and max range as the codebook endpoints
            var start = new Vector3(0.0f);
            var end = new Vector3(0.0f);
            if (count > 0)
            {
                float max;

                // compute the range
                start = end = values[0];
                var min = max = Vector3.Dot(values[0], principle);
                for (var i = 1; i < count; ++i)
                {
                    var val = Vector3.Dot(values[i], principle);
                    if (val < min)
                    {
                        start = values[i];
                        min = val;
                    }
                    else if (val > max)
                    {
                        end = values[i];
                        max = val;
                    }
                }
            }

            // clamp the output to [0, 1]
            var one = new Vector3(1.0f);
            var zero = new Vector3(0.0f);
            start = Vector3.Min(one, Vector3.Max(zero, start));
            end = Vector3.Min(one, Vector3.Max(zero, end));

            // clamp to the grid and save
            var grid = new Vector3(31.0f, 63.0f, 31.0f);
            var gridrcp = new Vector3(1.0f / 31.0f, 1.0f / 63.0f, 1.0f / 31.0f);
            var half = new Vector3(0.5f);
            _mStart = Helpers.Truncate(grid * start + half) * gridrcp;
            _mEnd = Helpers.Truncate(grid * end + half) * gridrcp;
        }

        protected override void Compress3(ref byte[] block, int offset)
        {
            // cache some values
            var count = MColours.Count;
            var values = MColours.Points;

            // create a codebook
            var codes = new Vector3[3];
            codes[0] = _mStart;
            codes[1] = _mEnd;
            codes[2] = 0.5f * _mStart + 0.5f * _mEnd;

            // match each point to the closest code
            var closest = new byte[16];
            var error = 0.0f;
            for (var i = 0; i < count; ++i)
            {
                // find the closest code
                var dist = float.MaxValue;
                var idx = 0;
                for (var j = 0; j < 3; ++j)
                {
                    var d = (_mMetric * (values[i] - codes[j])).LengthSquared();
                    if (d < dist)
                    {
                        dist = d;
                        idx = j;
                    }
                }

                // save the index
                closest[i] = (byte)idx;

                // accumulate the error
                error += dist;
            }

            // save this scheme if it wins
            if (error < _mBesterror)
            {
                // remap the indices
                var indices = new byte[16];
                MColours.RemapIndices(closest, indices);

                // save the block
                ColourBlock.WriteColourBlock3(_mStart, _mEnd, indices, ref block, offset);

                // save the error
                _mBesterror = error;
            }
        }

        protected override void Compress4(ref byte[] block, int offset) 
        {
            throw new NotImplementedException("RangeFit.Compress4");
        }
    }
}
