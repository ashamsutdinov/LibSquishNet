using System.Numerics;

namespace LibSquishNet
{
    public class Sym3X3
    {
        private readonly float[] _mX = new float[6];

        public float this[int i]
        {
            get { return _mX[i]; }
            set { _mX[i] = value; }
        }

        public Sym3X3(float s)
        {
            for (var i = 0; i < 6; i++) { _mX[i] = s; }
        }

        public static Sym3X3 ComputeWeightedCovariance(int n, Vector3[] points, float[] weights)
        {
            // compute the centroid
            var total = 0.0f;
            var centroid = new Vector3(0.0f);
            for (var i = 0; i < n; ++i)
            {
                total += weights[i];
                centroid += weights[i] * points[i];
            }
            if (total > float.Epsilon) { centroid /= total; }

            // accumulate the covariance matrix
            var covariance = new Sym3X3(0.0f);
            for (var i = 0; i < n; ++i)
            {
                var a = points[i] - centroid;
                var b = weights[i] * a;

                covariance[0] += a.X * b.X;
                covariance[1] += a.X * b.Y;
                covariance[2] += a.X * b.Z;
                covariance[3] += a.Y * b.Y;
                covariance[4] += a.Y * b.Z;
                covariance[5] += a.Z * b.Z;
            }

            // return it
            return covariance;
        }

        public static Vector3 ComputePrincipleComponent(Sym3X3 matrix)
        {
            var row0 = new Vector4(matrix[0], matrix[1], matrix[2], 0.0f);
            var row1 = new Vector4(matrix[1], matrix[3], matrix[4], 0.0f);
            var row2 = new Vector4(matrix[2], matrix[4], matrix[5], 0.0f);
            var v = new Vector4(1.0f);
            for (var i = 0; i < 8; ++i)
            {
                // matrix multiply
                var w = row0 * v.SplatX();
                w = Helpers.MultiplyAdd(row1, v.SplatY(), w);
                w = Helpers.MultiplyAdd(row2, v.SplatZ(), w);

                // get max component from xyz in all channels
                var a = Vector4.Max(w.SplatX(), Vector4.Max(w.SplatY(), w.SplatZ()));

                // divide through and advance
                v = w * Helpers.Reciprocal(a);
            }

            return v.ToVector3();
        }
    }
}
