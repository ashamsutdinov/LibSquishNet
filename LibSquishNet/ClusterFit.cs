using System.Numerics;

namespace LibSquishNet
{
    public class ClusterFit :
        ColourFit
    {
        private readonly int _mIterationCount;

        private readonly Vector3 _mPrinciple;

        private readonly byte[] _mOrder = new byte[16 * 8];

        private readonly Vector4[] _mPointsWeights = new Vector4[16];

        private Vector4 _mXsumWsum;

        private readonly Vector4 _mMetric;

        private Vector4 _mBesterror;

        public ClusterFit(ColourSet colours, SquishFlags flags, float? metric)
            : base(colours, flags)
        {
            // set the iteration count
            _mIterationCount = ((MFlags & SquishFlags.KColourIterativeClusterFit) != 0 ? 8 : 1);

            // initialise the metric (old perceptual = 0.2126f, 0.7152f, 0.0722f)
            if (metric != null)
            {
                //m_metric = Vec4( metric[0], metric[1], metric[2], 1.0f );
            }
            else
            {
                _mMetric = new Vector4(1.0f);
            }

            // initialise the best error
            _mBesterror = new Vector4(float.MaxValue);

            // cache some values
            var count = MColours.Count;
            var values = MColours.Points;

            // get the covariance matrix
            var covariance = Sym3X3.ComputeWeightedCovariance(count, values, MColours.Weights);

            // compute the principle component
            _mPrinciple = Sym3X3.ComputePrincipleComponent(covariance);
        }

        public bool ConstructOrdering(Vector3 axis, int iteration)
        {
            // cache some values
            var count = MColours.Count;
            var values = MColours.Points;

            // build the list of dot products
            var dps = new float[16];

            for (var i = 0; i < count; ++i)
            {
                dps[i] = Vector3.Dot(values[i], axis);
                _mOrder[(16 * iteration) + i] = (byte)i;
            }

            // stable sort using them
            for (var i = 0; i < count; ++i)
            {
                for (var j = i; j > 0 && dps[j] < dps[j - 1]; --j)
                {
                    var tf = dps[j];
                    dps[j] = dps[j - 1];
                    dps[j - 1] = tf;

                    var tb = _mOrder[(16 * iteration) + j];
                    _mOrder[(16 * iteration) + j] = _mOrder[(16 * iteration) + j - 1];
                    _mOrder[(16 * iteration) + j - 1] = tb;
                }
            }

            // check this ordering is unique
            for (var it = 0; it < iteration; ++it)
            {
                var same = true;
                for (var i = 0; i < count; ++i)
                {
                    if (_mOrder[(16 * iteration) + i] != _mOrder[(16 * it) + i])
                    {
                        same = false;
                        break;
                    }
                }
                if (same)
                    return false;
            }

            // copy the ordering and weight all the points
            var unweighted = MColours.Points;
            var weights = MColours.Weights;
            _mXsumWsum = new Vector4(0.0f);
            for (var i = 0; i < count; ++i)
            {
                int j = _mOrder[(16 * iteration) + i];
                var p = new Vector4(unweighted[j].X, unweighted[j].Y, unweighted[j].Z, 1.0f);
                var w = new Vector4(weights[j]);
                var x = p * w;
                _mPointsWeights[i] = x;
                _mXsumWsum += x;
            }
            return true;
        }

        public override void Compress3(ref byte[] block, int offset)
        {
            // declare variables
            var count = MColours.Count;
            var two = new Vector4(2.0f);
            var one = new Vector4(1.0f);
            var halfHalf2 = new Vector4(0.5f, 0.5f, 0.5f, 0.25f);
            var zero = new Vector4(0.0f);
            var half = new Vector4(0.5f);
            var grid = new Vector4(31.0f, 63.0f, 31.0f, 0.0f);
            var gridrcp = new Vector4(1.0f / 31.0f, 1.0f / 63.0f, 1.0f / 31.0f, 0.0f);

            // prepare an ordering using the principle axis
            ConstructOrdering(_mPrinciple, 0);

            // check all possible clusters and iterate on the total order
            var beststart = new Vector4(0.0f);
            var bestend = new Vector4(0.0f);
            var besterror = _mBesterror;
            var bestindices = new byte[16];
            var bestiteration = 0;
            int besti = 0, bestj = 0;

            // loop over iterations (we avoid the case that all points in first or last cluster)
            for (var iterationIndex = 0; ; )
            {
                // first cluster [0,i) is at the start
                var part0 = new Vector4(0.0f);
                for (var i = 0; i < count; ++i)
                {
                    // second cluster [i,j) is half along
                    var part1 = (i == 0) ? _mPointsWeights[0] : new Vector4(0.0f);
                    var jmin = (i == 0) ? 1 : i;
                    for (var j = jmin; ; )
                    {
                        // last cluster [j,count) is at the end
                        var part2 = _mXsumWsum - part1 - part0;

                        // compute least squares terms directly
                        var alphaxSum = Helpers.MultiplyAdd(part1, halfHalf2, part0);
                        var alpha2Sum = alphaxSum.SplatW();

                        var betaxSum = Helpers.MultiplyAdd(part1, halfHalf2, part2);
                        var beta2Sum = betaxSum.SplatW();

                        var alphabetaSum = (part1 * halfHalf2).SplatW();

                        // compute the least-squares optimal points
                        var factor = Helpers.Reciprocal(Helpers.NegativeMultiplySubtract(alphabetaSum, alphabetaSum, alpha2Sum * beta2Sum));
                        var a = Helpers.NegativeMultiplySubtract(betaxSum, alphabetaSum, alphaxSum * beta2Sum) * factor;
                        var b = Helpers.NegativeMultiplySubtract(alphaxSum, alphabetaSum, betaxSum * alpha2Sum) * factor;

                        // clamp to the grid
                        a = Vector4.Min(one, Vector4.Max(zero, a));
                        b = Vector4.Min(one, Vector4.Max(zero, b));
                        a = Helpers.Truncate(Helpers.MultiplyAdd(grid, a, half)) * gridrcp;
                        b = Helpers.Truncate(Helpers.MultiplyAdd(grid, b, half)) * gridrcp;

                        // compute the error (we skip the constant xxsum)
                        var e1 = Helpers.MultiplyAdd(a * a, alpha2Sum, b * b * beta2Sum);
                        var e2 = Helpers.NegativeMultiplySubtract(a, alphaxSum, a * b * alphabetaSum);
                        var e3 = Helpers.NegativeMultiplySubtract(b, betaxSum, e2);
                        var e4 = Helpers.MultiplyAdd( two, e3, e1);

                        // apply the metric to the error term
                        var e5 = e4 * _mMetric;
                        var error = e5.SplatX() + e5.SplatY() + e5.SplatZ();

                        // keep the solution if it wins
                        if (Helpers.CompareAnyLessThan(error, besterror))
                        {
                            beststart = a;
                            bestend = b;
                            besti = i;
                            bestj = j;
                            besterror = error;
                            bestiteration = iterationIndex;
                        }

                        // advance
                        if (j == count)
                            break;
                        part1 += _mPointsWeights[j];
                        ++j;
                    }

                    // advance
                    part0 += _mPointsWeights[i];
                }

                // stop if we didn't improve in this iteration
                if (bestiteration != iterationIndex)
                    break;

                // advance if possible
                ++iterationIndex;
                if (iterationIndex == _mIterationCount)
                    break;

                // stop if a new iteration is an ordering that has already been tried
                var axis = (bestend - beststart).ToVector3();
                if (!ConstructOrdering(axis, iterationIndex))
                    break;
            }

            // save the block if necessary
            if (Helpers.CompareAnyLessThan(besterror, _mBesterror))
            {
                var unordered = new byte[16];
                for (var m = 0; m < besti; ++m)
                    unordered[_mOrder[(16 * bestiteration) + m]] = 0;
                for (var m = besti; m < bestj; ++m)
                    unordered[_mOrder[(16 * bestiteration) + m]] = 2;
                for (var m = bestj; m < count; ++m)
                    unordered[_mOrder[(16 * bestiteration) + m]] = 1;

                MColours.RemapIndices(unordered, bestindices);

                // save the block
                ColourBlock.WriteColourBlock3(beststart.ToVector3(), bestend.ToVector3(), bestindices, ref block, offset);

                // save the error
                _mBesterror = besterror;
            }
        }

        public override void Compress4(ref byte[] block, int offset)
        {
            // declare variables
            var count = MColours.Count;
            var two = new Vector4(2.0f);
            var one = new Vector4(1.0f);
            var onethirdOnethird2 = new Vector4(1.0f / 3.0f, 1.0f / 3.0f, 1.0f / 3.0f, 1.0f / 9.0f);
            var twothirdsTwothirds2 = new Vector4(2.0f / 3.0f, 2.0f / 3.0f, 2.0f / 3.0f, 4.0f / 9.0f);
            var twonineths = new Vector4(2.0f / 9.0f);
            var zero = new Vector4(0.0f);
            var half = new Vector4(0.5f);
            var grid = new Vector4(31.0f, 63.0f, 31.0f, 0.0f);
            var gridrcp = new Vector4(1.0f / 31.0f, 1.0f / 63.0f, 1.0f / 31.0f, 0.0f);

            // prepare an ordering using the principle axis
            ConstructOrdering(_mPrinciple, 0);

            // check all possible clusters and iterate on the total order
            var beststart = new Vector4(0.0f);
            var bestend = new Vector4(0.0f);
            var besterror = _mBesterror;
            var bestindices = new byte[16];
            var bestiteration = 0;
            int besti = 0, bestj = 0, bestk = 0;

            // loop over iterations (we avoid the case that all points in first or last cluster)
            for (var iterationIndex = 0; ; )
            {
                // first cluster [0,i) is at the start
                var part0 = new Vector4(0.0f);
                for (var i = 0; i < count; ++i)
                {
                    // second cluster [i,j) is one third along
                    var part1 = new Vector4(0.0f);
                    for (var j = i; ; )
                    {
                        // third cluster [j,k) is two thirds along
                        var part2 = (j == 0) ? _mPointsWeights[0] : new Vector4(0.0f);
                        var kmin = (j == 0) ? 1 : j;
                        for (var k = kmin; ; )
                        {
                            // last cluster [k,count) is at the end
                            var part3 = _mXsumWsum - part2 - part1 - part0;

                            // compute least squares terms directly
                            var alphaxSum = Helpers.MultiplyAdd(part2, onethirdOnethird2, Helpers.MultiplyAdd(part1, twothirdsTwothirds2, part0));
                            var alpha2Sum = alphaxSum.SplatW();

                            var betaxSum = Helpers.MultiplyAdd(part1, onethirdOnethird2, Helpers.MultiplyAdd(part2, twothirdsTwothirds2, part3));
                            var beta2Sum = betaxSum.SplatW();

                            var alphabetaSum = twonineths * (part1 + part2).SplatW();

                            // compute the least-squares optimal points
                            var factor = Helpers.Reciprocal(Helpers.NegativeMultiplySubtract(alphabetaSum, alphabetaSum, alpha2Sum * beta2Sum));
                            var a = Helpers.NegativeMultiplySubtract(betaxSum, alphabetaSum, alphaxSum * beta2Sum) * factor;
                            var b = Helpers.NegativeMultiplySubtract(alphaxSum, alphabetaSum, betaxSum * alpha2Sum) * factor;

                            // clamp to the grid
                            a = Vector4.Min(one, Vector4.Max(zero, a));
                            b = Vector4.Min(one, Vector4.Max(zero, b));
                            a = Helpers.Truncate(Helpers.MultiplyAdd(grid, a, half)) * gridrcp;
                            b = Helpers.Truncate(Helpers.MultiplyAdd(grid, b, half)) * gridrcp;

                            // compute the error (we skip the constant xxsum)
                            var e1 = Helpers.MultiplyAdd(a * a, alpha2Sum, b * b * beta2Sum);
                            var e2 = Helpers.NegativeMultiplySubtract(a, alphaxSum, a * b * alphabetaSum);
                            var e3 = Helpers.NegativeMultiplySubtract(b, betaxSum, e2);
                            var e4 = Helpers.MultiplyAdd(two, e3, e1);

                            // apply the metric to the error term
                            var e5 = e4 * _mMetric;
                            var error = e5.SplatX() + e5.SplatY() + e5.SplatZ();

                            // keep the solution if it wins
                            if (Helpers.CompareAnyLessThan(error, besterror))
                            {
                                beststart = a;
                                bestend = b;
                                besterror = error;
                                besti = i;
                                bestj = j;
                                bestk = k;
                                bestiteration = iterationIndex;
                            }

                            // advance
                            if (k == count)
                                break;
                            part2 += _mPointsWeights[k];
                            ++k;
                        }

                        // advance
                        if (j == count)
                            break;
                        part1 += _mPointsWeights[j];
                        ++j;
                    }

                    // advance
                    part0 += _mPointsWeights[i];
                }

                // stop if we didn't improve in this iteration
                if (bestiteration != iterationIndex)
                    break;

                // advance if possible
                ++iterationIndex;
                if (iterationIndex == _mIterationCount)
                    break;

                // stop if a new iteration is an ordering that has already been tried
                var axis = (bestend - beststart).ToVector3();
                if (!ConstructOrdering(axis, iterationIndex))
                    break;
            }

            // save the block if necessary
            if (Helpers.CompareAnyLessThan(besterror, _mBesterror))
            {
                // remap the indices
                var unordered = new byte[16];
                for (var m = 0; m < besti; ++m)
                    unordered[_mOrder[(16 * bestiteration) + m]] = 0;
                for (var m = besti; m < bestj; ++m)
                    unordered[_mOrder[(16 * bestiteration) + m]] = 2;
                for (var m = bestj; m < bestk; ++m)
                    unordered[_mOrder[(16 * bestiteration) + m]] = 3;
                for (var m = bestk; m < count; ++m)
                    unordered[_mOrder[(16 * bestiteration) + m]] = 1;

                MColours.RemapIndices(unordered, bestindices);

                // save the block
                ColourBlock.WriteColourBlock4(beststart.ToVector3(), bestend.ToVector3(), bestindices, ref block, offset);

                // save the error
                _mBesterror = besterror;
            }
        }
    }
}
