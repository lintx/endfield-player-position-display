using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using endfield_player_position_display.Models;
using endfield_player_position_display.Services;
using endfield_player_position_display.ViewModels;

namespace endfield_player_position_display
{
    public partial class SeamEstimateWindow : Window
    {
        private readonly DispatcherTimer timer = new DispatcherTimer();
        private readonly List<SeamHeightSample> samples = new List<SeamHeightSample>();
        private readonly MainViewModel viewModel;
        private double currentX;
        private double currentHeight;
        private double currentZ;
        private double startX;
        private double startHeight;
        private double startZ;
        private double targetHeight;
        private DateTimeOffset countdownEndTime;
        private DateTimeOffset sessionStartTime;
        private bool isCountdown;
        private bool isRunning;
        private bool hasReport;
        private bool hasLiveHeight;

        public SeamEstimateWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            this.viewModel = viewModel;
            timer.Interval = TimeSpan.FromMilliseconds(250);
            timer.Tick += TimerTick;
            Loaded += WindowLoaded;
            Closed += WindowClosed;
            if (this.viewModel != null)
            {
                this.viewModel.PropertyChanged += ViewModelPropertyChanged;
            }
        }

        private void WindowLoaded(object sender, RoutedEventArgs e)
        {
            RefreshLiveHeight();
            UpdateUi();
        }

        private void WindowClosed(object sender, EventArgs e)
        {
            timer.Stop();
            if (viewModel != null)
            {
                viewModel.PropertyChanged -= ViewModelPropertyChanged;
            }
        }

        private void StartButtonClick(object sender, RoutedEventArgs e)
        {
            if (isCountdown || isRunning)
            {
                return;
            }

            RefreshLiveHeight();
            if (!hasLiveHeight)
            {
                StatusText.Text = "等待 websocket 坐标数据";
                UpdateUi();
                return;
            }

            if (string.IsNullOrWhiteSpace(TargetHeightTextBox.Text))
            {
                StatusText.Text = "请先填写目标高度";
                UpdateUi();
                return;
            }

            double parsedTarget;
            if (!TryParseHeight(TargetHeightTextBox.Text, out parsedTarget))
            {
                StatusText.Text = "目标高度格式不正确";
                return;
            }

            targetHeight = parsedTarget;
            startX = currentX;
            startHeight = currentHeight;
            startZ = currentZ;
            samples.Clear();
            hasReport = false;
            isCountdown = true;
            countdownEndTime = DateTimeOffset.Now.AddSeconds(3);
            CountdownText.Text = "倒计时：3 秒";
            StatusText.Text = string.Format(
                CultureInfo.InvariantCulture,
                "起始高度 {0}，目标高度 {1}，高度差 {2}",
                FormatPosition(startHeight, startX, startZ),
                FormatHeight(targetHeight),
                FormatHeight(Math.Abs(targetHeight - startHeight)));
            timer.Start();
            UpdateUi();
        }

        private void EndButtonClick(object sender, RoutedEventArgs e)
        {
            if (!isCountdown && !isRunning)
            {
                return;
            }

            FinishSession("手动结束");
        }

        private void ManualMarkButtonClick(object sender, RoutedEventArgs e)
        {
            if (!isRunning)
            {
                return;
            }

            RefreshLiveHeight();
            if (!hasLiveHeight)
            {
                StatusText.Text = "等待 websocket 坐标数据";
                return;
            }

            samples.Add(CreateCurrentSample(DateTimeOffset.Now, true));
            SeamMotionEstimate estimate = SeamMotionAnalyzer.Analyze(samples, targetHeight);
            UpdateEstimateText(estimate);
            StatusText.Text = "已手动打点";
        }

        private void TimerTick(object sender, EventArgs e)
        {
            DateTimeOffset now = DateTimeOffset.Now;

            if (isCountdown)
            {
                int remaining = (int)Math.Ceiling((countdownEndTime - now).TotalSeconds);
                if (remaining <= 0)
                {
                    BeginSession(now);
                    return;
                }

                CountdownText.Text = "倒计时：" + remaining + " 秒";
                UpdateUi();
                return;
            }

            if (!isRunning)
            {
                return;
            }

            RefreshLiveHeight();
            if (!hasLiveHeight)
            {
                StatusText.Text = "等待 websocket 坐标数据";
                return;
            }

            samples.Add(CreateCurrentSample(now, false));

            SeamMotionEstimate estimate = SeamMotionAnalyzer.Analyze(samples, targetHeight);
            UpdateEstimateText(estimate);
            UpdateCurrentHeightText();

            if (HasReachedTarget(currentHeight, targetHeight))
            {
                FinishSession("已到达目标高度");
            }
        }

        private void BeginSession(DateTimeOffset now)
        {
            isCountdown = false;
            isRunning = true;
            sessionStartTime = now;
            samples.Clear();
            samples.Add(new SeamHeightSample(sessionStartTime, startX, startHeight, startZ));
            timer.Interval = TimeSpan.FromMilliseconds(250);
            StatusText.Text = "运行中";
            CountdownText.Text = string.Empty;
            UpdateCurrentHeightText();
            UpdateEstimateText(SeamMotionAnalyzer.Analyze(samples, targetHeight));
            UpdateUi();
        }

        private void FinishSession(string reason)
        {
            timer.Stop();
            isCountdown = false;
            isRunning = false;
            RefreshLiveHeight();

            DateTimeOffset now = DateTimeOffset.Now;
            if (hasLiveHeight)
            {
                AppendFinalSample(now);
            }
            else if (!samples.Any())
            {
                samples.Add(CreateCurrentSample(now, false));
            }

            SeamMotionReport report = SeamMotionAnalyzer.BuildReport(samples);
            StatusText.Text = reason;
            CountdownText.Text = string.Empty;
            UpdateEstimateText(SeamMotionAnalyzer.Analyze(samples, targetHeight));
            ReportTextBox.Text = BuildReportText(report);
            hasReport = true;
            UpdateUi();
        }

        private void UpdateUi()
        {
            StartButton.IsEnabled = hasLiveHeight && !isCountdown && !isRunning;
            EndButton.IsEnabled = isCountdown || isRunning;
            ManualMarkButton.IsEnabled = hasLiveHeight && isRunning;
            TargetHeightTextBox.IsEnabled = hasLiveHeight && !isCountdown && !isRunning;
            if (!hasReport && !isRunning)
            {
                ReportTextBox.Text = "开始后会在这里显示最终报告。";
            }
        }

        private void UpdateCurrentHeightText()
        {
            CurrentHeightText.Text = hasLiveHeight
                ? FormatPosition(currentHeight, currentX, currentZ)
                : "等待 websocket 坐标";
        }

        private void UpdateEstimateText(SeamMotionEstimate estimate)
        {
            if (estimate == null || estimate.RemainingTime == null)
            {
                EstimateText.Text = "还没有足够数据用于预估。";
                return;
            }

            TimeSpan remaining = estimate.RemainingTime.Value;
            EstimateText.Text = string.Format(
                CultureInfo.InvariantCulture,
                "还剩下 {0} 米，预计还要 {1} 分 {2} 秒，当前预估速度 {3} 米/秒",
                FormatDistance(estimate.RemainingHeight),
                remaining.Minutes + remaining.Hours * 60,
                remaining.Seconds,
                FormatDistance(estimate.EstimatedSpeed));
        }

        private string BuildReportText(SeamMotionReport report)
        {
            if (report == null)
            {
                return "没有生成报告。";
            }

            var builder = new StringBuilder();
            builder.AppendLine("起始高度：" + FormatPosition(report.StartHeight, report.StartX, report.StartZ));
            builder.AppendLine("起始时间：" + report.StartTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
            builder.AppendLine("结束高度：" + FormatPosition(report.EndHeight, report.EndX, report.EndZ));
            builder.AppendLine("结束时间：" + report.EndTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
            builder.AppendLine("总高度：" + FormatDistance(report.TotalDistance));
            builder.AppendLine("总时间：" + FormatDuration(report.TotalDuration));
            builder.AppendLine("平均速度：" + FormatDistance(report.AverageSpeed) + " 米/秒");
            builder.AppendLine();
            builder.AppendLine("分段：");
            if (report.Segments.Count == 0)
            {
                builder.AppendLine("  无有效移动分段");
                return builder.ToString();
            }

            for (int i = 0; i < report.Segments.Count; i++)
            {
                SeamMotionSegment segment = report.Segments[i];
                builder.AppendLine(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "  {0}. {1} -> {2} | {3} - {4} | {5} 米/秒{6}",
                        i + 1,
                        FormatPosition(segment.StartHeight, segment.StartX, segment.StartZ),
                        FormatPosition(segment.EndHeight, segment.EndX, segment.EndZ),
                        segment.StartTime.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
                        segment.EndTime.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
                        FormatDistance(segment.Speed),
                        string.IsNullOrWhiteSpace(segment.Reason) ? string.Empty : " | " + segment.Reason));
            }

            return builder.ToString();
        }

        private static bool HasReachedTarget(double current, double target)
        {
            return Math.Abs(current - target) <= 0.02;
        }

        private static string FormatHeight(double value)
        {
            return value.ToString("0.000", CultureInfo.InvariantCulture);
        }

        private static string FormatPosition(double height, double x, double z)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} ({1},{2})",
                FormatHeight(height),
                FormatCoordinate(x),
                FormatCoordinate(z));
        }

        private static string FormatCoordinate(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string FormatDistance(double value)
        {
            return value.ToString("0.0000", CultureInfo.InvariantCulture);
        }

        private static string FormatDuration(TimeSpan duration)
        {
            if (duration < TimeSpan.Zero)
            {
                duration = duration.Negate();
            }

            return duration.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
        }

        private static bool TryParseHeight(string text, out double value)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }

        private void ViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(e.PropertyName) && !string.Equals(e.PropertyName, "CurrentPosition", StringComparison.Ordinal))
            {
                return;
            }

            Dispatcher.Invoke(RefreshLiveHeight);
        }

        private void RefreshLiveHeight()
        {
            if (viewModel != null && viewModel.CurrentPosition != null)
            {
                currentX = viewModel.CurrentPosition.X;
                currentHeight = viewModel.CurrentPosition.Y;
                currentZ = viewModel.CurrentPosition.Z;
                hasLiveHeight = true;
            }
            else
            {
                hasLiveHeight = false;
            }

            UpdateCurrentHeightText();
        }

        private SeamHeightSample CreateCurrentSample(DateTimeOffset timestamp, bool manualBreak)
        {
            return new SeamHeightSample(timestamp, currentX, currentHeight, currentZ, manualBreak);
        }

        private void AppendFinalSample(DateTimeOffset timestamp)
        {
            SeamHeightSample finalSample = CreateCurrentSample(timestamp, false);
            if (samples.Count > 0)
            {
                SeamHeightSample last = samples[samples.Count - 1];
                if (last.Timestamp == finalSample.Timestamp
                    && Math.Abs(last.X - finalSample.X) <= 0.000001
                    && Math.Abs(last.Height - finalSample.Height) <= 0.000001
                    && Math.Abs(last.Z - finalSample.Z) <= 0.000001)
                {
                    return;
                }
            }

            samples.Add(finalSample);
        }
    }
}
