using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections;
using System.Collections.Specialized;

using Ecng.Common;

using StockSharp.Algo;
using StockSharp.Algo.Candles;
using StockSharp.Algo.Indicators;
using StockSharp.Algo.Strategies;
using StockSharp.Logging;
using StockSharp.BusinessEntities;
using StockSharp.Messages;
using StockSharp.Xaml.Charting;
using StockSharp.Localization;



namespace TradingStrategies
{
    public class FuturesStrategy : Strategy
    {
        private IChart strategyChart;
        private ChartCandleElement strategyCandlesChart;
        private ChartTradeElement strategyTradesChart;

        private ICollection<Tuple<string, BaseIndicator, ChartIndicatorElement>> strategyIndicators; // Name, Indicator, Indicator chart. Null if no chart need.
        private List<MyTrade> strategyTradesList = new List<MyTrade>();

        private CandleSeries strategySeries;
        private bool _isShortLessThenLong;

        public FuturesStrategy(IChart chart, ChartCandleElement candlesElem, ChartTradeElement tradesElem, 
                               ICollection<Tuple<string, BaseIndicator, ChartIndicatorElement>> indicators, CandleSeries series)
        {
            strategyChart = chart;
            strategyCandlesChart = candlesElem;
            strategyTradesChart = tradesElem;

            strategyIndicators = indicators;
            strategySeries = series;
        }

        protected override void OnStarted()
        {
            strategySeries
                .WhenCandlesFinished()
                .Do(ProcessCandle)
                .Apply(this);

            this
                .WhenNewMyTrades()
                .Do(trades => strategyTradesList.AddRange(trades))
                .Apply(this);

            base.OnStarted();
        }

        private void ProcessCandle(Candle candle)
        {
            if (ProcessState == ProcessStates.Stopping)
            {
                CancelActiveOrders();
                return;
            }

            this.AddInfoLog(LocalizedStrings.Str3634Params.Put(candle.OpenTime, candle.OpenPrice, candle.HighPrice, candle.LowPrice, candle.ClosePrice, candle.TotalVolume, candle.Security));

            var trade = strategyTradesList.FirstOrDefault();
            strategyTradesList.Clear();

            var dict = new Dictionary<IChartElement, object>()
            {
                { strategyCandlesChart, candle },
                { strategyTradesChart, trade }
            };

            foreach(var elem in strategyIndicators)
            {
                dict.Add(elem.Item3, elem.Item2.GetCurrentValue());
            }

            strategyChart.Draw(candle.OpenTime, dict);
        }
    }





    class SmaStrategy : Strategy
    {
        private readonly IChart _chart;
        private readonly ChartCandleElement _candlesElem;
        private readonly ChartTradeElement _tradesElem;
        private readonly ChartIndicatorElement _shortElem;
        private readonly ChartIndicatorElement _longElem;
        private readonly List<MyTrade> _myTrades = new List<MyTrade>();
        private readonly CandleSeries _series;
        private bool _isShortLessThenLong;

        public SmaStrategy(IChart chart, ChartCandleElement candlesElem, ChartTradeElement tradesElem,
            SimpleMovingAverage shortMa, ChartIndicatorElement shortElem,
            SimpleMovingAverage longMa, ChartIndicatorElement longElem,
            CandleSeries series)
        {
            _chart = chart;
            _candlesElem = candlesElem;
            _tradesElem = tradesElem;
            _shortElem = shortElem;
            _longElem = longElem;

            _series = series;

            ShortSma = shortMa;
            LongSma = longMa;
        }

        public SimpleMovingAverage LongSma { get; private set; }
        public SimpleMovingAverage ShortSma { get; private set; }

        protected override void OnStarted()
        {
            _series
                .WhenCandlesFinished()
                .Do(ProcessCandle)
                .Apply(this);

            this
                .WhenNewMyTrades()
                .Do(trades => _myTrades.AddRange(trades))
                .Apply(this);

            // store current values for short and long
            _isShortLessThenLong = ShortSma.GetCurrentValue() < LongSma.GetCurrentValue();

            base.OnStarted();
        }

        private void ProcessCandle(Candle candle)
        {
            // strategy are stopping
            if (ProcessState == ProcessStates.Stopping)
            {
                CancelActiveOrders();
                return;
            }

            this.AddInfoLog(LocalizedStrings.Str3634Params.Put(candle.OpenTime, candle.OpenPrice, candle.HighPrice, candle.LowPrice, candle.ClosePrice, candle.TotalVolume, candle.Security));

            // process new candle
            var longValue = LongSma.Process(candle);
            var shortValue = ShortSma.Process(candle);

            // calc new values for short and long
            var isShortLessThenLong = ShortSma.GetCurrentValue() < LongSma.GetCurrentValue();

            // crossing happened
            if (_isShortLessThenLong != isShortLessThenLong)
            {
                // if short less than long, the sale, otherwise buy
                var direction = isShortLessThenLong ? Sides.Sell : Sides.Buy;

                // calc size for open position or revert
                var volume = Position == 0 ? Volume : Position.Abs().Min(Volume) * 2;

                if (!SafeGetConnector().RegisteredMarketDepths.Contains(Security))
                {
                    var price = Security.GetMarketPrice(Connector, direction);

                    // register "market" order (limit order with guaranteed execution price)
                    if (price != null)
                        RegisterOrder(this.CreateOrder(direction, price.Value, volume));
                }
                else
                {
                    // register order (limit order)
                    RegisterOrder(this.CreateOrder(direction, (decimal)(Security.GetCurrentPrice(this, direction) ?? 0), volume));

                    // or revert position via market quoting
                    //var strategy = new MarketQuotingStrategy(direction, volume)
                    //{
                    //	WaitAllTrades = true,
                    //};
                    //ChildStrategies.Add(strategy);
                }

                // store current values for short and long
                _isShortLessThenLong = isShortLessThenLong;
            }

            var trade = _myTrades.FirstOrDefault();
            _myTrades.Clear();

            var dict = new Dictionary<IChartElement, object>
            {
                { _candlesElem, candle },
                { _shortElem, shortValue },
                { _longElem, longValue },
                { _tradesElem, trade }
            };

            _chart.Draw(candle.OpenTime, dict);
        }
    }
}