﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Navigation;
using Newtonsoft.Json;
using StockAnalyzer.Core.Domain;

namespace StockAnalyzer.Windows
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        CancellationTokenSource cancellationTokenSource = null;

        private void Search_Click(object sender, RoutedEventArgs e)
        {
            // async void is evil, the only case to use is for event handlers
            #region Before loading stock data
            var watch = new Stopwatch();
            watch.Start();
            StockProgress.Visibility = Visibility.Visible;
            StockProgress.IsIndeterminate = true;
            #endregion

            if (cancellationTokenSource != null)
            {
                cancellationTokenSource.Cancel();
                cancellationTokenSource = null;
                return;
            }

            cancellationTokenSource = new CancellationTokenSource();

            cancellationTokenSource.Token.Register(() =>
            {
                Notes.Text = "Cancellation requested";
            });

            var loadLinesTask = SearchForStocks(cancellationTokenSource.Token);

            // Continuation
            var processStocksTask = loadLinesTask.ContinueWith(t =>
            {
                var lines = t.Result; // After a task is awaited, you can get its Result

                var data = new List<StockPrice>();

                foreach (var line in lines.Skip(1))
                {
                    var segments = line.Split(',');

                    for (var i = 0; i < segments.Length; i++) segments[i] = segments[i].Trim('\'', '"');
                    var price = new StockPrice
                    {
                        Ticker = segments[0],
                        TradeDate = DateTime.ParseExact(segments[1], "M/d/yyyy h:mm:ss tt", CultureInfo.InvariantCulture),
                        Volume = Convert.ToInt32(segments[6], CultureInfo.InvariantCulture),
                        Change = Convert.ToDecimal(segments[7], CultureInfo.InvariantCulture),
                        ChangePercent = Convert.ToDecimal(segments[8], CultureInfo.InvariantCulture),
                    };
                    data.Add(price);
                }

                Dispatcher.Invoke(() =>
                {
                    // return back to Thread
                    Stocks.ItemsSource = data.Where(price => price.Ticker == Ticker.Text);
                });
            }, 
            cancellationTokenSource.Token,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.Current);

            loadLinesTask.ContinueWith(t =>
            {
                Dispatcher.Invoke(() =>
                {
                    Notes.Text = t.Exception.InnerException.Message;
                });
            }, TaskContinuationOptions.OnlyOnFaulted);

            processStocksTask.ContinueWith(_ =>
            {
                Dispatcher.Invoke(() =>
                {
                    // return back to Thread
                    #region After stock data is loaded
                    StocksStatus.Text = $"Loaded stocks for {Ticker.Text} in {watch.ElapsedMilliseconds}ms";
                    StockProgress.Visibility = Visibility.Hidden;
                    #endregion
                });
            });

        }

        private Task<List<string>> SearchForStocks(CancellationToken cancellationToken)
        {
            return Task.Run(async () =>
            {
                var lines = new List<string>();

                using (var stream = new StreamReader(File.OpenRead(@"C:\Users\qing.ma\Projects\StockAnalyzer\StockAnalyzer.Web\StockPrices_Small.csv")))
                {
                    string line;
                    while ((line = await stream.ReadLineAsync()) != null)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            return lines;
                        }

                        lines.Add(line);
                    }
                }

                return lines;
            }, cancellationToken);
        }

        private void Hyperlink_OnRequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri));

            e.Handled = true;
        }

        private void Close_OnClick(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        public async Task GetStocks()
        {
            using (var client = new HttpClient())
            {
                var response = await client.GetAsync($"http://localhost:61363/api/stocks/{Ticker.Text}");
                // await gives you a potential result
                try
                {
                    response.EnsureSuccessStatusCode(); // await validates the success of the operation

                    var content = await response.Content.ReadAsStringAsync();

                    var data = JsonConvert.DeserializeObject<IEnumerable<StockPrice>>(content);

                    Stocks.ItemsSource = data;
                }
                catch (Exception ex)
                {
                    // continuation is back on calling thread
                    Notes.Text += ex.Message;
                }
            }
        }
    }
}
