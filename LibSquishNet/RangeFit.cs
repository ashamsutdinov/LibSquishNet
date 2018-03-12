using System;
using System.Numerics;

namespace LibSquishNet
{
    public class RangeFit : ColourFit
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
            int count = MColours.Count;
            Vector3[] values = MColours.Points;
            float[] weights = MColours.Weights;

            // get the covariance matrix
            Sym3X3 covariance = Sym3X3.ComputeWeightedCovariance(count, values, weights);

            // compute the principle component
            Vector3 principle = Sym3X3.ComputePrincipleComponent(covariance);

            // get the min and max range as the codebook endpoints
            Vector3 start = new Vector3(0.0f);
            Vector3 end = new Vector3(0.0f);
            if (count > 0)
            {
                float min, max;

                // compute the range
                start = end = values[0];
                min = max = Vector3.Dot(values[0], principle);
                for (int i = 1; i < count; ++i)
                {
                    float val = Vector3.Dot(values[i], principle);
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
            Vector3 one = new Vector3(1.0f);
            Vector3 zero = new Vector3(0.0f);
            start = Vector3.Min(one, Vector3.Max(zero, start));
            end = Vector3.Min(one, Vector3.Max(zero, end));

            // clamp to the grid and save
            Vector3 grid = new Vector3(31.0f, 63.0f, 31.0f);
            Vector3 gridrcp = new Vector3(1.0f / 31.0f, 1.0f / 63.0f, 1.0f / 31.0f);
            Vector3 half = new Vector3(0.5f);
            _mStart = Helpers.Truncate(grid * start + half) * gridrcp;
            _mEnd = Helpers.Truncate(grid * end + half) * gridrcp;
        }

        public override void Compress3(ref byte[] block, int offset)
        {
            // cache some values
            int count = MColours.Count;
            Vector3[] values = MColours.Points;

            // create a codebook
            Vector3[] codes = new Vector3[3];
            codes[0] = _mStart;
            codes[1] = _mEnd;
            codes[2] = 0.5f * _mStart + 0.5f * _mEnd;

            // match each point to the closest code
            byte[] closest = new byte[16];
            float error = 0.0f;
            for (int i = 0; i < count; ++i)
            {
                // find the closest code
                float dist = float.MaxValue;
                int idx = 0;
                for (int j = 0; j < 3; ++j)
                {
                    float d = (_mMetric * (values[i] - codes[j])).LengthSquared();
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
                byte[] indices = new byte[16];
                MColours.RemapIndices(closest, indices);

                // save the block
                ColourBlock.WriteColourBlock3(_mStart, _mEnd, indices, ref block, offset);

                // save the error
                _mBesterror = error;
            }
        }

        public override void Compress4(ref byte[] block, int offset) 
        {
            throw new NotImplementedException("RangeFit.Compress4");
        }
    }
}
