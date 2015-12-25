using System.ComponentModel;
using System.Linq;
using StockSharp.Localization;
using StockSharp.Algo;
using StockSharp.Algo.Indicators;
using System;

using AForge;
using AForge.Math;
using AForge.Math.Random;

using MathNet;
using MathNet.Numerics;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;
using MathNet.Numerics.Random;

using StockSharp.Algo.Candles;




namespace Indicators
{
    public class RecursiveKalmanFilter : LengthIndicator<decimal>
    {
        private Matrix<double> Pplus, Pminus;
        private Matrix<double> Q, R, F, H;
        private Vector<double> xplus, xminus;
        private double fadingCoeff;

        private int xDim, yDim;

        public Matrix<double> Qmatrix { get { return Q.Clone(); } set { Qmatrix.CopyTo(Q); } }
        public Matrix<double> Rmatrix { get { return R.Clone(); } set { Rmatrix.CopyTo(R); } }
        public double FadingCoeff { get { return fadingCoeff; } set { fadingCoeff = value; } }


        public RecursiveKalmanFilter(int xDim, int yDim)
        {
            this.xDim = xDim;
            this.yDim = yDim;

            Pplus = DenseMatrix.Create(xDim, xDim, delegate (int i, int j) { return 100000000.0; });
            Pminus = DenseMatrix.Create(xDim, xDim, delegate (int i, int j) { return 100000000.0; });

            Q = DenseMatrix.Create(xDim, xDim, delegate (int i, int j) { return 10.0; });
            R = DenseMatrix.Create(yDim, yDim, delegate (int i, int j) { return 10.0; });
            F = DenseMatrix.Create(xDim, xDim, delegate (int i, int j) { return 1.0; });
            H = DenseMatrix.Create(yDim, xDim, delegate (int i, int j) { return 1.0; });

            xplus = DenseVector.Create(xDim, delegate (int i) { return 0; });
            xminus = DenseVector.Create(xDim, delegate (int i) { return 0; });

            fadingCoeff = 1.01;

            IsFormed = false;
        }

        private void RecalculateFilterParameters(Vector<double> y)
        {

            Pminus = Math.Pow(fadingCoeff, 2) * F * Pplus * F.Transpose() + Q;

            var tempVal = (H * Pminus * H.Transpose() + R).Inverse();
            var K = Pminus * H.Transpose() * tempVal;

            xminus = F * xplus;

            xplus = xminus + K * (y - H * xminus);

            Pplus = Pminus - K * H * Pminus;
        }

        protected override IIndicatorValue OnProcess(IIndicatorValue input)
        {
            
            var candle = input.GetValue<Candle>();
            var medianPrice = (candle.ClosePrice + candle.OpenPrice + candle.HighPrice + candle.LowPrice) / 4m;

            Buffer.Add(medianPrice);

            Vector<double> val = DenseVector.Create(1, (i) => 0);
            val[0] = (double)medianPrice;

            RecalculateFilterParameters(val);

            if (Buffer.Count < 5)
                return new DecimalIndicatorValue(this, medianPrice);

            IsFormed = true;

            return new DecimalIndicatorValue(this, (decimal)xplus[0]);
        }

        public Vector<double> GetEstimatedValue()
        {
            return xplus;
        }
    }
}