namespace SampleHistoryTesting
{
	using System;
	using System.IO;
	using System.Linq;
	using System.Windows;
	using System.Windows.Controls;
	using System.Windows.Media;
	using System.Collections.Generic;

	using Ecng.Xaml;
	using Ecng.Common;
	using Ecng.Collections;
	using Ecng.Localization;

	using Ookii.Dialogs.Wpf;

	using StockSharp.Algo;
	using StockSharp.Algo.Candles;
	using StockSharp.Algo.Candles.Compression;
	using StockSharp.Algo.Commissions;
	using StockSharp.Algo.Storages;
	using StockSharp.Algo.Testing;
	using StockSharp.Algo.Indicators;
	using StockSharp.BusinessEntities;
	using StockSharp.Logging;
	using StockSharp.Messages;
	using StockSharp.Xaml.Charting;
	using StockSharp.Localization;

    using TradingStrategies;

    public partial class MainWindow
	{
        private HistoryEmulationConnector connector;
		private BufferedChart bufferedChart;
		
		private DateTime startEmulationTime;
		private ChartCandleElement candlesElem;
		private ChartTradeElement tradesElem;
		private ChartIndicatorElement shortElem;
		private SimpleMovingAverage shortMa;
		private ChartIndicatorElement longElem;
		private SimpleMovingAverage longMa;
		private ChartArea area;

        private string StrategyName = "SMA Strategy";
    
		public MainWindow()
		{
			InitializeComponent();

			bufferedChart = new BufferedChart(Chart);
            connector = null;

			HistoryPath.Text = @"D:\StockSharp\DatabaseNew\".ToFullPath();

			if (LocalizedStrings.ActiveLanguage == Languages.Russian)
			{
				SecId.Text = "RIZ5@FORTS";

				From.Value = new DateTime(2015, 10, 1);
				To.Value = new DateTime(2015, 11, 5);
			}
		}

		private void FindPathClick(object sender, RoutedEventArgs e)
		{
			var dlg = new VistaFolderBrowserDialog();

			if (!HistoryPath.Text.IsEmpty())
				dlg.SelectedPath = HistoryPath.Text;

			if (dlg.ShowDialog(this) == true)
			{
				HistoryPath.Text = dlg.SelectedPath;
			}
		}

		private void StartBtnClick(object sender, RoutedEventArgs e)
		{
			InitChart();

			if (HistoryPath.Text.IsEmpty() || !Directory.Exists(HistoryPath.Text))
			{
				MessageBox.Show(this, LocalizedStrings.Str3014);
				return;
			}

			var secGen = new SecurityIdGenerator();
			var secIdParts = secGen.Split(SecId.Text);

			var storageRegistry = new StorageRegistry() { DefaultDrive = new LocalMarketDataDrive(HistoryPath.Text)	};

			var startTime = ((DateTime)From.Value).ChangeKind(DateTimeKind.Utc);
			var stopTime = ((DateTime)To.Value).ChangeKind(DateTimeKind.Utc);

            //var logManager = new LogManager();
            //var fileLogListener = new FileLogListener("sample.log");
            //logManager.Listeners.Add(fileLogListener);
            //logManager.Listeners.Add(new DebugLogListener());	// for track logs in output window in Vusial Studio (poor performance).

            var maxDepth = 5;
            var maxVolume = 5;

			var secCode = secIdParts.Item1;
			var board = ExchangeBoard.GetOrCreateBoard(secIdParts.Item2);

            var progressBar = TicksTestingProcess;
            var progressStep = ((stopTime - startTime).Ticks / 100).To<TimeSpan>();
            progressBar.Value = 0;
            progressBar.Maximum = 100;

            var statistic = TicksParameterGrid;
    		var security = new Security()
			{
				Id = SecId.Text, 
				Code = secCode,
				Board = board,
			};

			var portfolio = new Portfolio()
			{
				Name = "test account",
				BeginValue = 1000000,
			};

			var connector = new HistoryEmulationConnector(new[] { security }, new[] { portfolio })
			{
				EmulationAdapter =
				{
					Emulator =
					{
						Settings =	{ MatchOnTouch = true, PortfolioRecalcInterval = TimeSpan.FromMilliseconds(100), SpreadSize = 1, },
                        LogLevel = LogLevels.Debug,
					},
                    LogLevel = LogLevels.Debug, 
				},
				HistoryMessageAdapter = { StorageRegistry = storageRegistry, StartDate = startTime, StopDate = stopTime, MarketTimeChangedInterval = TimeSpan.FromMilliseconds(50), },
			};

			//logManager.Sources.Add(connector);

			var candleManager = new CandleManager(connector);
            var series = new CandleSeries(typeof(RangeCandle), security, new Unit(100));

			shortMa = new SimpleMovingAverage { Length = 10 };
			shortElem = new ChartIndicatorElement
			{
				Color = Colors.Coral,
				ShowAxisMarker = false,
				FullTitle = shortMa.ToString()
			};
			bufferedChart.AddElement(area, shortElem);

			longMa = new SimpleMovingAverage { Length = 30 };
			longElem = new ChartIndicatorElement
			{
				ShowAxisMarker = false,
				FullTitle = longMa.ToString()
			};

            bufferedChart.AddElement(area, longElem);

            var strategy = new SmaStrategy(bufferedChart, candlesElem, tradesElem, shortMa, shortElem, longMa, longElem, series)
            {
                Volume = 1,
                Portfolio = portfolio,
                Security = security,
                Connector = connector,
                LogLevel = LogLevels.Debug, 
			    UnrealizedPnLInterval = ((stopTime - startTime).Ticks / 1000).To<TimeSpan>()
			};

			//logManager.Sources.Add(strategy);

			connector.NewSecurities += securities =>
			{
			    if (securities.All(s => s != security))
                    return;

                connector.RegisterMarketDepth(security);
                connector.RegisterMarketDepth(new TrendMarketDepthGenerator(connector.GetSecurityId(security))
				{
				    Interval = TimeSpan.FromMilliseconds(100), // order book freq refresh is 1 sec
				    MaxAsksDepth = maxDepth,
				    MaxBidsDepth = maxDepth,
				    UseTradeVolume = true,
				    MaxVolume = maxVolume,
				    MinSpreadStepCount = 1,	// min spread generation is 2 pips
				    MaxSpreadStepCount = 1,	// max spread generation size (prevent extremely size)
                    MaxPriceStepCount = 3	// pips size,
               });

               connector.RegisterTrades(security);
			   connector.RegisterSecurity(security);

			   strategy.Start();
			   candleManager.Start(series);

			   connector.Start();
            };

            statistic.Parameters.Clear();
			statistic.Parameters.AddRange(strategy.StatisticManager.Parameters);                

			var pnlCurve = Curve.CreateCurve(LocalizedStrings.PnL + " " + StrategyName, Colors.Cyan, EquityCurveChartStyles.Area);
			var unrealizedPnLCurve = Curve.CreateCurve(LocalizedStrings.PnLUnreal + StrategyName, Colors.Black);
			var commissionCurve = Curve.CreateCurve(LocalizedStrings.Str159 + " " + StrategyName, Colors.Red, EquityCurveChartStyles.DashedLine);
			var posItems = PositionCurve.CreateCurve(StrategyName, Colors.Crimson);
			strategy.PnLChanged += () =>
			{
				var pnl = new EquityData() { Time = strategy.CurrentTime, Value = strategy.PnL - strategy.Commission ?? 0 };
				var unrealizedPnL = new EquityData() { Time = strategy.CurrentTime, Value = strategy.PnLManager.UnrealizedPnL };
				var commission = new EquityData() { Time = strategy.CurrentTime, Value = strategy.Commission ?? 0 };

				pnlCurve.Add(pnl);
				unrealizedPnLCurve.Add(unrealizedPnL);
				commissionCurve.Add(commission);
			};

			strategy.PositionChanged += () => posItems.Add(new EquityData { Time = strategy.CurrentTime, Value = strategy.Position });

			var nextTime = startTime + progressStep;

			connector.MarketTimeChanged += d =>
			{
				if (connector.CurrentTime < nextTime && connector.CurrentTime < stopTime)
					return;

				var steps = (connector.CurrentTime - startTime).Ticks / progressStep.Ticks + 1;
				nextTime = startTime + (steps * progressStep.Ticks).To<TimeSpan>();
				this.GuiAsync(() => progressBar.Value = steps);
			};

			connector.StateChanged += () =>
			{
				if (connector.State == EmulationStates.Stopped)
				{
					candleManager.Stop(series);
					strategy.Stop();

					//logManager.Dispose();

					SetIsEnabled(false);

					this.GuiAsync(() =>
					{
						if (connector.IsFinished)
						{
							progressBar.Value = progressBar.Maximum;
							MessageBox.Show(this, LocalizedStrings.Str3024.Put(DateTime.Now - startEmulationTime));
						}
						else
							MessageBox.Show(this, LocalizedStrings.cancelled);
					});
				}
				else if (connector.State == EmulationStates.Started)
				{
					SetIsEnabled(true);
				}
			};

			progressBar.Value = 0;
    		startEmulationTime = DateTime.Now;

		    connector.Connect();
		    connector.SendInMessage(new CommissionRuleMessage() { Rule = new CommissionPerTradeRule { Value = 0.01m } });
	    }

		private void StopBtnClick(object sender, RoutedEventArgs e)
		{
    		connector.Disconnect();
		}

		private void InitChart()
		{
			bufferedChart.ClearAreas();
			Curve.Clear();
			PositionCurve.Clear();

			area = new ChartArea();
			bufferedChart.AddArea(area);

			candlesElem = new ChartCandleElement { ShowAxisMarker = false };
			bufferedChart.AddElement(area, candlesElem);

			tradesElem = new ChartTradeElement { FullTitle = "Сделки",  };
			bufferedChart.AddElement(area, tradesElem);
		}

		private void SetIsEnabled(bool started)
		{
			this.GuiAsync(() =>
			{
				StopBtn.IsEnabled = started;
				StartBtn.IsEnabled = !started;

				bufferedChart.IsAutoRange = started;                
			});
		}
	}
}