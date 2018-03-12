using System.Numerics;

namespace LibSquishNet
{
    public class ClusterFit : ColourFit
    {
        readonly int _mIterationCount;
        readonly Vector3 _mPrinciple;
        readonly byte[] _mOrder = new byte[16 * 8];
        readonly Vector4[] _mPointsWeights = new Vector4[16];
        Vector4 _mXsumWsum;
        readonly Vector4 _mMetric;
        Vector4 _mBesterror;

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
            int count = MColours.Count;
            Vector3[] values = MColours.Points;

            // get the covariance matrix
            Sym3X3 covariance = Sym3X3.ComputeWeightedCovariance(count, values, MColours.Weights);

            // compute the principle component
            _mPrinciple = Sym3X3.ComputePrincipleComponent(covariance);
        }

        public bool ConstructOrdering(Vector3 axis, int iteration)
        {
            // cache some values
            int count = MColours.Count;
            Vector3[] values = MColours.Points;

            // build the list of dot products
            float[] dps = new float[16];

            for (int i = 0; i < count; ++i)
            {
                dps[i] = Vector3.Dot(values[i], axis);
                _mOrder[(16 * iteration) + i] = (byte)i;
            }

            // stable sort using them
            for (int i = 0; i < count; ++i)
            {
                for (int j = i; j > 0 && dps[j] < dps[j - 1]; --j)
                {
                    float tf = dps[j];
                    dps[j] = dps[j - 1];
                    dps[j - 1] = tf;

                    byte tb = _mOrder[(16 * iteration) + j];
                    _mOrder[(16 * iteration) + j] = _mOrder[(16 * iteration) + j - 1];
                    _mOrder[(16 * iteration) + j - 1] = tb;
                }
            }

            // check this ordering is unique
            for (int it = 0; it < iteration; ++it)
            {
                bool same = true;
                for (int i = 0; i < count; ++i)
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
            Vector3[] unweighted = MColours.Points;
            float[] weights = MColours.Weights;
            _mXsumWsum = new Vector4(0.0f);
            for (int i = 0; i < count; ++i)
            {
                int j = _mOrder[(16 * iteration) + i];
                Vector4 p = new Vector4(unweighted[j].X, unweighted[j].Y, unweighted[j].Z, 1.0f);
                Vector4 w = new Vector4(weights[j]);
                Vector4 x = p * w;
                _mPointsWeights[i] = x;
                _mXsumWsum += x;
            }
            return true;
        }

        public override void Compress3(ref byte[] block, int offset)
        {
            // declare variables
            int count = MColours.Count;
            Vector4 two = new Vector4(2.0f);
            Vector4 one = new Vector4(1.0f);
            Vector4 halfHalf2 = new Vector4(0.5f, 0.5f, 0.5f, 0.25f);
            Vector4 zero = new Vector4(0.0f);
            Vector4 half = new Vector4(0.5f);
            Vector4 grid = new Vector4(31.0f, 63.0f, 31.0f, 0.0f);
            Vector4 gridrcp = new Vector4(1.0f / 31.0f, 1.0f / 63.0f, 1.0f / 31.0f, 0.0f);

            // prepare an ordering using the principle axis
            ConstructOrdering(_mPrinciple, 0);

            // check all possible clusters and iterate on the total order
            Vector4 beststart = new Vector4(0.0f);
            Vector4 bestend = new Vector4(0.0f);
            Vector4 besterror = _mBesterror;
            byte[] bestindices = new byte[16];
            int bestiteration = 0;
            int besti = 0, bestj = 0;

            // loop over iterations (we avoid the case that all points in first or last cluster)
            for (int iterationIndex = 0; ; )
            {
                // first cluster [0,i) is at the start
                Vector4 part0 = new Vector4(0.0f);
                for (int i = 0; i < count; ++i)
                {
                    // second cluster [i,j) is half along
                    Vector4 part1 = (i == 0) ? _mPointsWeights[0] : new Vector4(0.0f);
                    int jmin = (i == 0) ? 1 : i;
                    for (int j = jmin; ; )
                    {
                        // last cluster [j,count) is at the end
                        Vector4 part2 = _mXsumWsum - part1 - part0;

                        // compute least squares terms directly
                        Vector4 alphaxSum = Helpers.MultiplyAdd(part1, halfHalf2, part0);
                        Vector4 alpha2Sum = alphaxSum.SplatW();

                        Vector4 betaxSum = Helpers.MultiplyAdd(part1, halfHalf2, part2);
                        Vector4 beta2Sum = betaxSum.SplatW();

                        Vector4 alphabetaSum = (part1 * halfHalf2).SplatW();

                        // compute the least-squares optimal points
                        Vector4 factor = Helpers.Reciprocal(Helpers.NegativeMultiplySubtract(alphabetaSum, alphabetaSum, alpha2Sum * beta2Sum));
                        Vector4 a = Helpers.NegativeMultiplySubtract(betaxSum, alphabetaSum, alphaxSum * beta2Sum) * factor;
                        Vector4 b = Helpers.NegativeMultiplySubtract(alphaxSum, alphabetaSum, betaxSum * alpha2Sum) * factor;

                        // clamp to the grid
                        a = Vector4.Min(one, Vector4.Max(zero, a));
                        b = Vector4.Min(one, Vector4.Max(zero, b));
                        a = Helpers.Truncate(Helpers.MultiplyAdd(grid, a, half)) * gridrcp;
                        b = Helpers.Truncate(Helpers.MultiplyAdd(grid, b, half)) * gridrcp;

                        // compute the error (we skip the constant xxsum)
                        Vector4 e1 = Helpers.MultiplyAdd(a * a, alpha2Sum, b * b * beta2Sum);
                        Vector4 e2 = Helpers.NegativeMultiplySubtract(a, alphaxSum, a * b * alphabetaSum);
                        Vector4 e3 = Helpers.NegativeMultiplySubtract(b, betaxSum, e2);
                        Vector4 e4 = Helpers.MultiplyAdd( two, e3, e1);

                        // apply the metric to the error term
                        Vector4 e5 = e4 * _mMetric;
                        Vector4 error = e5.SplatX() + e5.SplatY() + e5.SplatZ();

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
                Vector3 axis = (bestend - beststart).ToVector3();
                if (!ConstructOrdering(axis, iterationIndex))
                    break;
            }

            // save the block if necessary
            if (Helpers.CompareAnyLessThan(besterror, _mBesterror))
            {
                byte[] unordered = new byte[16];
                for (int m = 0; m < besti; ++m)
                    unordered[_mOrder[(16 * bestiteration) + m]] = 0;
                for (int m = besti; m < bestj; ++m)
                    unordered[_mOrder[(16 * bestiteration) + m]] = 2;
                for (int m = bestj; m < count; ++m)
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
            int count = MColours.Count;
            Vector4 two = new Vector4(2.0f);
            Vector4 one = new Vector4(1.0f);
            Vector4 onethirdOnethird2 = new Vector4(1.0f / 3.0f, 1.0f / 3.0f, 1.0f / 3.0f, 1.0f / 9.0f);
            Vector4 twothirdsTwothirds2 = new Vector4(2.0f / 3.0f, 2.0f / 3.0f, 2.0f / 3.0f, 4.0f / 9.0f);
            Vector4 twonineths = new Vector4(2.0f / 9.0f);
            Vector4 zero = new Vector4(0.0f);
            Vector4 half = new Vector4(0.5f);
            Vector4 grid = new Vector4(31.0f, 63.0f, 31.0f, 0.0f);
            Vector4 gridrcp = new Vector4(1.0f / 31.0f, 1.0f / 63.0f, 1.0f / 31.0f, 0.0f);

            // prepare an ordering using the principle axis
            ConstructOrdering(_mPrinciple, 0);

            // check all possible clusters and iterate on the total order
            Vector4 beststart = new Vector4(0.0f);
            Vector4 bestend = new Vector4(0.0f);
            Vector4 besterror = _mBesterror;
            byte[] bestindices = new byte[16];
            int bestiteration = 0;
            int besti = 0, bestj = 0, bestk = 0;

            // loop over iterations (we avoid the case that all points in first or last cluster)
            for (int iterationIndex = 0; ; )
            {
                // first cluster [0,i) is at the start
                Vector4 part0 = new Vector4(0.0f);
                for (int i = 0; i < count; ++i)
                {
                    // second cluster [i,j) is one third along
                    Vector4 part1 = new Vector4(0.0f);
                    for (int j = i; ; )
                    {
                        // third cluster [j,k) is two thirds along
                        Vector4 part2 = (j == 0) ? _mPointsWeights[0] : new Vector4(0.0f);
                        int kmin = (j == 0) ? 1 : j;
                        for (int k = kmin; ; )
                        {
                            // last cluster [k,count) is at the end
                            Vector4 part3 = _mXsumWsum - part2 - part1 - part0;

                            // compute least squares terms directly
                            Vector4 alphaxSum = Helpers.MultiplyAdd(part2, onethirdOnethird2, Helpers.MultiplyAdd(part1, twothirdsTwothirds2, part0));
                            Vector4 alpha2Sum = alphaxSum.SplatW();

                            Vector4 betaxSum = Helpers.MultiplyAdd(part1, onethirdOnethird2, Helpers.MultiplyAdd(part2, twothirdsTwothirds2, part3));
                            Vector4 beta2Sum = betaxSum.SplatW();

                            Vector4 alphabetaSum = twonineths * (part1 + part2).SplatW();

                            // compute the least-squares optimal points
                            Vector4 factor = Helpers.Reciprocal(Helpers.NegativeMultiplySubtract(alphabetaSum, alphabetaSum, alpha2Sum * beta2Sum));
                            Vector4 a = Helpers.NegativeMultiplySubtract(betaxSum, alphabetaSum, alphaxSum * beta2Sum) * factor;
                            Vector4 b = Helpers.NegativeMultiplySubtract(alphaxSum, alphabetaSum, betaxSum * alpha2Sum) * factor;

                            // clamp to the grid
                            a = Vector4.Min(one, Vector4.Max(zero, a));
                            b = Vector4.Min(one, Vector4.Max(zero, b));
                            a = Helpers.Truncate(Helpers.MultiplyAdd(grid, a, half)) * gridrcp;
                            b = Helpers.Truncate(Helpers.MultiplyAdd(grid, b, half)) * gridrcp;

                            // compute the error (we skip the constant xxsum)
                            Vector4 e1 = Helpers.MultiplyAdd(a * a, alpha2Sum, b * b * beta2Sum);
                            Vector4 e2 = Helpers.NegativeMultiplySubtract(a, alphaxSum, a * b * alphabetaSum);
                            Vector4 e3 = Helpers.NegativeMultiplySubtract(b, betaxSum, e2);
                            Vector4 e4 = Helpers.MultiplyAdd(two, e3, e1);

                            // apply the metric to the error term
                            Vector4 e5 = e4 * _mMetric;
                            Vector4 error = e5.SplatX() + e5.SplatY() + e5.SplatZ();

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
                Vector3 axis = (bestend - beststart).ToVector3();
                if (!ConstructOrdering(axis, iterationIndex))
                    break;
            }

            // save the block if necessary
            if (Helpers.CompareAnyLessThan(besterror, _mBesterror))
            {
                // remap the indices
                byte[] unordered = new byte[16];
                for (int m = 0; m < besti; ++m)
                    unordered[_mOrder[(16 * bestiteration) + m]] = 0;
                for (int m = besti; m < bestj; ++m)
                    unordered[_mOrder[(16 * bestiteration) + m]] = 2;
                for (int m = bestj; m < bestk; ++m)
                    unordered[_mOrder[(16 * bestiteration) + m]] = 3;
                for (int m = bestk; m < count; ++m)
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
