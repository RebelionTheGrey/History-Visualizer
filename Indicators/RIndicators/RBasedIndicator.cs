using System.ComponentModel;
using System.Linq;
using StockSharp.Localization;
using StockSharp.Algo;
using StockSharp.Algo.Indicators;
using System;
using StockSharp.Algo.Candles;

using RManaged;
using RManaged.Core;
using BaseTypes;

namespace Indicators
{
    public abstract class RLenghtIndicator<T> : LengthIndicator<T>
    {
        public IREngine Engine { get; protected set; }
        public RLenghtIndicator(IREngine engine):base()
        {
            Engine = engine;
        }
    }
}