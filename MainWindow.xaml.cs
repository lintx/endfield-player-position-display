using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using endfield_player_position_display.Models;
using endfield_player_position_display.Services;
using endfield_player_position_display.ViewModels;

namespace endfield_player_position_display
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel viewModel = new MainViewModel();
        private readonly UserSettingsStore settingsStore = new UserSettingsStore();
        private readonly SklandApiClient apiClient = new SklandApiClient();
        private readonly ClipboardTextService clipboardTextService = new ClipboardTextService();
        private readonly GameWindowLocator gameWindowLocator = new GameWindowLocator();
        private readonly GlobalHotkeyService hotkeyService = new GlobalHotkeyService();
        private readonly PositionCaptureRecorder captureRecorder = new PositionCaptureRecorder(AppDomain.CurrentDomain.BaseDirectory);
        private readonly DispatcherTimer followTimer = new DispatcherTimer();
        private readonly List<ZiplineMark> captureMarks = new List<ZiplineMark>();
        private PositionMonitorService monitorService;
        private CoordinateWindow coordinateWindow;
        private DetectionToastWindow detectionToastWindow;
        private SeamEstimateWindow seamEstimateWindow;
        private ZiplineRealtimeDetector realtimeDetector;
        private bool isLoadingCaptureMarks;
        private bool isCapturing;
        private bool suppressControlEvents;
        private bool capturingHotkey;
        private bool coordinateWindowFollowMode;
        private bool isClosing;
        private bool isConnecting;
        private DateTimeOffset? gameNotForegroundSince;
        private int monitorRunId;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = viewModel;
            Loaded += MainWindowLoaded;
            Closed += MainWindowClosed;
            PreviewKeyDown += MainWindowPreviewKeyDown;
            followTimer.Interval = TimeSpan.FromMilliseconds(500);
            followTimer.Tick += FollowTimerTick;
        }

        private async void MainWindowLoaded(object sender, RoutedEventArgs e)
        {
            viewModel.ApplySettings(settingsStore.Load());
            UpdateMainUi();
            RegisterHotkey();
            ApplyCoordinateWindowState();
            if (!EnsureTokenAvailable())
            {
                viewModel.StatusText = "未登录";
                viewModel.WarningText = "登录后才能连接坐标同步";
                UpdateMainUi();
                return;
            }

            await StartMonitorAsync();
        }

        private void MainWindowClosed(object sender, EventArgs e)
        {
            isClosing = true;
            SaveSettings();
            followTimer.Stop();
            hotkeyService.Dispose();
            if (coordinateWindow != null)
            {
                coordinateWindow.Closed -= CoordinateWindowClosed;
                coordinateWindow.Close();
                coordinateWindow = null;
            }

            if (detectionToastWindow != null)
            {
                detectionToastWindow.Close();
                detectionToastWindow = null;
            }

            if (seamEstimateWindow != null)
            {
                seamEstimateWindow.Close();
                seamEstimateWindow = null;
            }

            if (monitorService != null)
            {
                monitorService.Dispose();
                monitorService = null;
            }

            apiClient.Dispose();
            captureRecorder.Dispose();
        }

        private void ApplyUpdate(MonitorUpdate update)
        {
            Dispatcher.Invoke(() =>
            {
                viewModel.ApplyMonitorUpdate(update);
                if (update.Position != null)
                {
                    if (captureRecorder.IsRecording)
                    {
                        captureRecorder.Record(update.Position, DateTimeOffset.UtcNow);
                    }

                    UpdateRealtimeDetection(update.Position);
                }

                if (update.IsError || update.SessionState != null || update.Position != null)
                {
                    isConnecting = false;
                }

                UpdateMainUi();
            });
        }

        private async void ReconnectButtonClick(object sender, RoutedEventArgs e)
        {
            if (!EnsureTokenAvailable())
            {
                viewModel.StatusText = "未登录";
                viewModel.WarningText = "登录后才能连接坐标同步";
                UpdateMainUi();
                return;
            }

            await StartMonitorAsync();
        }

        private async void TokenManagerButtonClick(object sender, RoutedEventArgs e)
        {
            var window = new TokenManagerWindow(AppDomain.CurrentDomain.BaseDirectory);
            window.Owner = this;
            window.ShowDialog();
            if (window.TokensChanged)
            {
                await RestartAfterTokenChangeAsync();
            }
        }

        private void SeamEstimateButtonClick(object sender, RoutedEventArgs e)
        {
            if (seamEstimateWindow != null)
            {
                seamEstimateWindow.Activate();
                return;
            }

            seamEstimateWindow = new SeamEstimateWindow(viewModel);
            seamEstimateWindow.Owner = this;
            seamEstimateWindow.Closed += SeamEstimateWindowClosed;
            seamEstimateWindow.Show();
        }

        private void SeamEstimateWindowClosed(object sender, EventArgs e)
        {
            if (seamEstimateWindow != null)
            {
                seamEstimateWindow.Closed -= SeamEstimateWindowClosed;
                seamEstimateWindow = null;
            }
        }

        private void RoleComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (suppressControlEvents)
            {
                return;
            }

            RoleSession role = RoleComboBox.SelectedItem as RoleSession;
            if (role != null)
            {
                viewModel.SelectedRoleKey = role.Key;
            }
        }

        private async void SwitchRoleButtonClick(object sender, RoutedEventArgs e)
        {
            RoleSession role = RoleComboBox.SelectedItem as RoleSession;
            if (role != null)
            {
                viewModel.SelectedRoleKey = role.Key;
            }

            await StartMonitorAsync();
        }

        private void CoordinateWindowChecked(object sender, RoutedEventArgs e)
        {
            if (suppressControlEvents)
            {
                return;
            }

            viewModel.IsCoordinateWindowOpen = true;
            ApplyCoordinateWindowState();
            SaveSettings();
        }

        private void CoordinateWindowUnchecked(object sender, RoutedEventArgs e)
        {
            if (suppressControlEvents)
            {
                return;
            }

            viewModel.IsCoordinateWindowOpen = false;
            ApplyCoordinateWindowState();
            SaveSettings();
        }

        private void FollowGameChanged(object sender, RoutedEventArgs e)
        {
            if (suppressControlEvents)
            {
                return;
            }

            viewModel.FollowGameWindow = FollowGameCheckBox.IsChecked == true;
            RecreateCoordinateWindowIfOpen();
            UpdateFollowTimer();
            SaveSettings();
            UpdateMainUi();
        }

        private void FollowPositionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (suppressControlEvents)
            {
                return;
            }

            viewModel.FollowPosition = GetSelectedFollowPosition();
            UpdateOffsetControls();
            UpdateFollowPosition();
            SaveSettings();
        }

        private void FollowOffsetChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (suppressControlEvents)
            {
                return;
            }

            viewModel.SetFollowOffset(
                viewModel.FollowPosition,
                FollowHorizontalOffsetSlider.Value,
                FollowVerticalOffsetSlider.Value);
            UpdateFollowPosition();
            SaveSettings();
            UpdateOffsetText();
        }

        private void CaptureHotkeyButtonClick(object sender, RoutedEventArgs e)
        {
            capturingHotkey = true;
            CaptureHotkeyButton.Content = "按一个键...";
            WarningText.Text = "请按下一个键作为切换坐标窗口的快捷键";
            Focus();
        }

        private void MainWindowPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!capturingHotkey)
            {
                return;
            }

            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.None || key == Key.LeftAlt || key == Key.RightAlt || key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftShift || key == Key.RightShift)
            {
                return;
            }

            capturingHotkey = false;
            viewModel.Hotkey = key;
            RegisterHotkey();
            SaveSettings();
            UpdateMainUi();
            e.Handled = true;
        }

        private async void LookupZiplineButtonClick(object sender, RoutedEventArgs e)
        {
            await ManualDetectZiplineAsync(false);
        }

        private async void ManualDetectZiplineButtonClick(object sender, RoutedEventArgs e)
        {
            await ManualDetectZiplineAsync(true);
        }

        private async Task ManualDetectZiplineAsync(bool addToCapture)
        {
            if (viewModel.CurrentPosition == null)
            {
                viewModel.ZiplineResult = ZiplineLookupResult.NotFound();
                ManualZiplineResultText.Text = "缺少当前位置，连接成功并获取坐标后再试";
                UpdateCopyButtons();
                return;
            }

            if (string.IsNullOrWhiteSpace(viewModel.MapId))
            {
                ManualZiplineResultText.Text = "缺少当前地图 ID，等待坐标同步后再试";
                UpdateCopyButtons();
                return;
            }

            if (viewModel.Credential == null || viewModel.RoleBinding == null)
            {
                ManualZiplineResultText.Text = "缺少登录或角色信息，连接成功后再试";
                UpdateCopyButtons();
                return;
            }

            ManualDetectZiplineButton.IsEnabled = false;
            ManualZiplineResultText.Text = "正在获取滑索坐标...";
            try
            {
                await EnsureCaptureMarksAsync();
                viewModel.ZiplineResult = ZiplineMatcher.FindNearest(viewModel.CurrentPosition, captureMarks);
                ManualZiplineResultText.Text = viewModel.ZiplineResult.Found
                    ? viewModel.ZiplineResult.ToTupleText()
                    : viewModel.ZiplineResult.Message;
                if (viewModel.ZiplineResult.Found && addToCapture)
                {
                    if (realtimeDetector == null)
                    {
                        realtimeDetector = new ZiplineRealtimeDetector(captureMarks);
                    }

                    ZiplineRealtimeDetection detection = realtimeDetector.AddManual(viewModel.CurrentPosition);
                    if (detection.Detected)
                    {
                        if (captureRecorder.IsRecording)
                        {
                            captureRecorder.WriteDetection(detection.Stop, DateTimeOffset.UtcNow);
                        }

                        ShowDetectionToast(detection.Stop);
                    }
                }
            }
            catch (Exception ex)
            {
                ManualZiplineResultText.Text = ex is InvalidOperationException ? ex.Message : "获取滑索坐标失败";
                viewModel.ZiplineResult = null;
            }
            finally
            {
                ManualDetectZiplineButton.IsEnabled = true;
                UpdateCopyButtons();
                UpdateMainUi();
            }
        }

        private void CopyTupleButtonClick(object sender, RoutedEventArgs e)
        {
            if (viewModel.ZiplineResult != null && viewModel.ZiplineResult.Found)
            {
                CopyText(viewModel.ZiplineResult.ToTupleText());
            }
        }

        private void CopyJsonButtonClick(object sender, RoutedEventArgs e)
        {
            if (viewModel.ZiplineResult != null && viewModel.ZiplineResult.Found)
            {
                CopyText(viewModel.ZiplineResult.ToJsonText());
            }
        }

        private void RecordCaptureDataChanged(object sender, RoutedEventArgs e)
        {
            if (suppressControlEvents)
            {
                return;
            }

            viewModel.RecordCaptureData = RecordCaptureDataCheckBox.IsChecked == true;
            SaveSettings();
            UpdateMainUi();
        }

        private void CopyText(string text)
        {
            string error;
            if (clipboardTextService.TrySetText(text, out error))
            {
                viewModel.WarningText = "已复制到剪贴板";
            }
            else
            {
                viewModel.WarningText = error;
            }

            UpdateMainUi();
        }

        private async void StartCaptureButtonClick(object sender, RoutedEventArgs e)
        {
            if (realtimeDetector != null && realtimeDetector.DetectedStops.Count > 0)
            {
                MessageBoxResult result = MessageBox.Show(
                    this,
                    "开始新的采集会清除当前已识别的滑索数据，是否继续？",
                    "确认开始采集",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning);
                if (result != MessageBoxResult.OK)
                {
                    return;
                }
            }

            isCapturing = true;
            if (viewModel.RecordCaptureData)
            {
                captureRecorder.Start(DateTimeOffset.Now);
            }
            else
            {
                captureRecorder.Stop();
            }

            captureMarks.Clear();
            realtimeDetector = null;
            isLoadingCaptureMarks = false;
            viewModel.ZiplineResult = null;
            ManualZiplineResultText.Text = string.Empty;
            RealtimeDetectionText.Text = string.Empty;
            viewModel.WarningText = "已开始采集，正在获取当前地图滑索数据...";
            UpdateMainUi();
            await LoadCaptureMarksAsync();
        }

        private void StopCaptureButtonClick(object sender, RoutedEventArgs e)
        {
            isCapturing = false;
            captureRecorder.Stop();
            viewModel.WarningText = "已停止采集";
            UpdateMainUi();
        }

        private async Task LoadCaptureMarksAsync()
        {
            if (!isCapturing)
            {
                return;
            }

            if (isLoadingCaptureMarks)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(viewModel.MapId) || viewModel.Credential == null || viewModel.RoleBinding == null)
            {
                viewModel.WarningText = "已开始采集；等待地图、登录和角色信息后再获取滑索数据";
                UpdateMainUi();
                return;
            }

            isLoadingCaptureMarks = true;
            try
            {
                IList<ZiplineMark> marks = await apiClient.GetZiplineMarksAsync(
                    viewModel.Credential,
                    viewModel.MapId,
                    viewModel.RoleBinding,
                    System.Threading.CancellationToken.None);
                captureMarks.Clear();
                captureMarks.AddRange(marks);
                if (captureRecorder.IsRecording)
                {
                    captureRecorder.WriteMarks(captureMarks);
                }

                realtimeDetector = new ZiplineRealtimeDetector(captureMarks);
                viewModel.WarningText = "已获取滑索数据：" + captureMarks.Count + " 个，停在滑索上等待识别";
            }
            catch (Exception ex)
            {
                viewModel.WarningText = ex is InvalidOperationException ? ex.Message : "获取采集用滑索数据失败";
            }
            finally
            {
                isLoadingCaptureMarks = false;
            }

            UpdateMainUi();
        }

        private async Task EnsureCaptureMarksAsync()
        {
            if (captureMarks.Count > 0)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(viewModel.MapId) || viewModel.Credential == null || viewModel.RoleBinding == null)
            {
                throw new InvalidOperationException("缺少地图、登录或角色信息");
            }

            IList<ZiplineMark> marks = await apiClient.GetZiplineMarksAsync(
                viewModel.Credential,
                viewModel.MapId,
                viewModel.RoleBinding,
                System.Threading.CancellationToken.None);
            captureMarks.Clear();
            captureMarks.AddRange(marks);
            if (captureRecorder.IsRecording)
            {
                captureRecorder.WriteMarks(captureMarks);
            }
        }

        private async void UpdateRealtimeDetection(PositionSnapshot position)
        {
            if (!isCapturing)
            {
                return;
            }

            if (realtimeDetector == null)
            {
                if (captureMarks.Count == 0 && !string.IsNullOrWhiteSpace(viewModel.MapId) && viewModel.Credential != null && viewModel.RoleBinding != null)
                {
                    await LoadCaptureMarksAsync();
                }

                return;
            }

            ZiplineRealtimeDetection detection = realtimeDetector.Update(position);
            if (!detection.Detected)
            {
                return;
            }

            if (captureRecorder.IsRecording)
            {
                captureRecorder.WriteDetection(detection.Stop, DateTimeOffset.UtcNow);
            }

            viewModel.WarningText = string.Format(
                CultureInfo.InvariantCulture,
                "已识别第 {0} 个滑索：({1},{2},{3},{4})",
                detection.Stop.Order,
                detection.Stop.X,
                detection.Stop.Y,
                detection.Stop.Z,
                detection.Stop.Direction);
            ShowDetectionToast(detection.Stop);
            UpdateMainUi();
        }

        private void CopyCaptureTextButtonClick(object sender, RoutedEventArgs e)
        {
            if (realtimeDetector != null)
            {
                CopyText(realtimeDetector.GetResultJson());
            }
        }

        private void CopyCaptureJsonButtonClick(object sender, RoutedEventArgs e)
        {
            if (realtimeDetector != null)
            {
                CopyText(realtimeDetector.GetRouteCollectionJson());
            }
        }

        private void ShowDetectionToast(DetectedZiplineStop stop)
        {
            if (stop == null)
            {
                return;
            }

            if (detectionToastWindow == null)
            {
                detectionToastWindow = new DetectionToastWindow();
            }

            Rect targetRect;
            if (!gameWindowLocator.TryGetEndfieldWindowRect(out targetRect))
            {
                targetRect = SystemParameters.WorkArea;
            }
            else
            {
                targetRect = ConvertGameRectToDips(targetRect);
            }

            detectionToastWindow.UpdateLayout();
            double left = targetRect.Right - detectionToastWindow.Width - 18;
            double top = targetRect.Bottom - detectionToastWindow.Height - 18;
            string message = string.Format(
                CultureInfo.InvariantCulture,
                "已识别滑索 ({0},{1},{2},{3})",
                stop.X,
                stop.Y,
                stop.Z,
                stop.Direction);
            detectionToastWindow.ShowMessage(message, left, top);
        }

        private void ToggleCoordinateWindow()
        {
            viewModel.IsCoordinateWindowOpen = !viewModel.IsCoordinateWindowOpen;
            ApplyCoordinateWindowState();
            SaveSettings();
            UpdateMainUi();
        }

        private void ApplyCoordinateWindowState()
        {
            if (viewModel.IsCoordinateWindowOpen)
            {
                if (coordinateWindow == null)
                {
                    coordinateWindowFollowMode = viewModel.FollowGameWindow;
                    coordinateWindow = new CoordinateWindow(viewModel, coordinateWindowFollowMode);
                    coordinateWindow.Closed += CoordinateWindowClosed;
                }

                if (!coordinateWindow.IsVisible)
                {
                    coordinateWindow.Show();
                }

                UpdateFollowPosition();
            }
            else if (coordinateWindow != null)
            {
                coordinateWindow.Closed -= CoordinateWindowClosed;
                coordinateWindow.Close();
                coordinateWindow = null;
            }

            UpdateFollowTimer();
            UpdateMainUi();
        }

        private void CoordinateWindowClosed(object sender, EventArgs e)
        {
            coordinateWindow.Closed -= CoordinateWindowClosed;
            coordinateWindow = null;
            if (isClosing)
            {
                return;
            }

            viewModel.IsCoordinateWindowOpen = false;
            UpdateFollowTimer();
            UpdateMainUi();
            SaveSettings();
        }

        private void RecreateCoordinateWindowIfOpen()
        {
            if (coordinateWindow == null)
            {
                return;
            }

            coordinateWindow.Closed -= CoordinateWindowClosed;
            coordinateWindow.Close();
            coordinateWindow = null;
            coordinateWindowFollowMode = viewModel.FollowGameWindow;
            coordinateWindow = new CoordinateWindow(viewModel, coordinateWindowFollowMode);
            coordinateWindow.Closed += CoordinateWindowClosed;
            coordinateWindow.Show();
            UpdateFollowPosition();
        }

        private void FollowTimerTick(object sender, EventArgs e)
        {
            UpdateFollowPosition();
        }

        private void UpdateFollowTimer()
        {
            if (viewModel.IsCoordinateWindowOpen && viewModel.FollowGameWindow)
            {
                if (!followTimer.IsEnabled)
                {
                    followTimer.Start();
                }
            }
            else
            {
                followTimer.Stop();
            }
        }

        private void UpdateFollowPosition()
        {
            if (coordinateWindow == null || !viewModel.FollowGameWindow)
            {
                return;
            }

            Rect gameRect;
            if (!gameWindowLocator.TryGetEndfieldWindowRect(out gameRect))
            {
                viewModel.WarningText = "未找到 endfield.exe 游戏窗口";
                coordinateWindow.Topmost = false;
                if (!coordinateWindow.IsVisible)
                {
                    coordinateWindow.Show();
                }

                gameNotForegroundSince = null;
                UpdateMainUi();
                return;
            }

            gameRect = ConvertGameRectToDips(gameRect);
            bool gameForeground = gameWindowLocator.IsEndfieldForeground();
            bool appForeground = IsActive || IsKeyboardFocusWithin || gameWindowLocator.IsCurrentProcessForeground();
            coordinateWindow.Topmost = gameForeground;
            if (gameForeground)
            {
                gameNotForegroundSince = null;
                if (!coordinateWindow.IsVisible)
                {
                    coordinateWindow.Show();
                }
            }
            else if (appForeground)
            {
                gameNotForegroundSince = null;
                coordinateWindow.Topmost = false;
                if (!coordinateWindow.IsVisible)
                {
                    coordinateWindow.Show();
                }
            }
            else
            {
                if (!gameNotForegroundSince.HasValue)
                {
                    gameNotForegroundSince = DateTimeOffset.UtcNow;
                }

                if ((DateTimeOffset.UtcNow - gameNotForegroundSince.Value).TotalSeconds >= 1)
                {
                    coordinateWindow.Hide();
                    UpdateMainUi();
                    return;
                }
            }

            viewModel.WarningText = string.Empty;
            coordinateWindow.UpdateLayout();
            double width = coordinateWindow.ActualWidth > 0 ? coordinateWindow.ActualWidth : coordinateWindow.Width;
            double height = coordinateWindow.ActualHeight > 0 ? coordinateWindow.ActualHeight : 70;
            FollowOffsetSettings offset = viewModel.GetFollowOffset(viewModel.FollowPosition);
            Point point = FollowWindowPlacement.Calculate(
                gameRect,
                new Size(width, height),
                viewModel.FollowPosition,
                offset.Horizontal,
                offset.Vertical);

            coordinateWindow.Left = point.X;
            coordinateWindow.Top = point.Y;
            UpdateMainUi();
        }

        private Rect ConvertGameRectToDips(Rect gameRect)
        {
            PresentationSource source = PresentationSource.FromVisual(this);
            if (source == null || source.CompositionTarget == null)
            {
                return gameRect;
            }

            Matrix transform = source.CompositionTarget.TransformFromDevice;
            return DpiCoordinateConverter.FromDevicePixels(gameRect, transform);
        }

        private void RegisterHotkey()
        {
            var helper = new WindowInteropHelper(this);
            if (helper.Handle == IntPtr.Zero)
            {
                return;
            }

            bool ok = hotkeyService.Register(helper.Handle, viewModel.Hotkey, ToggleCoordinateWindow);
            viewModel.WarningText = ok ? string.Empty : "快捷键被占用，注册失败";
        }

        private void SaveSettings()
        {
            settingsStore.Save(viewModel.ToSettings());
        }

        private async Task StartMonitorAsync()
        {
            if (isConnecting)
            {
                return;
            }

            isConnecting = true;
            int runId = ++monitorRunId;
            viewModel.StatusText = "正在连接...";
            viewModel.WarningText = string.Empty;
            viewModel.CurrentPosition = null;
            viewModel.Credential = null;
            viewModel.RoleBinding = null;
            viewModel.MapId = null;
            UpdateMainUi();

            if (monitorService != null)
            {
                monitorService.Dispose();
                monitorService = null;
            }

            monitorService = new PositionMonitorService(AppDomain.CurrentDomain.BaseDirectory, ApplyUpdate, viewModel.SelectedRoleKey);
            await Task.Run(() => monitorService.StartAsync());
            if (runId == monitorRunId)
            {
                isConnecting = false;
                UpdateMainUi();
            }
        }

        private bool EnsureTokenAvailable()
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            if (TokenFileWriter.HasAnyToken(baseDirectory))
            {
                return true;
            }

            var loginWindow = new LoginWindow();
            loginWindow.Owner = this;
            if (loginWindow.ShowDialog() == true)
            {
                TokenFileWriter.AppendToken(baseDirectory, loginWindow.Token);
                return true;
            }

            return false;
        }

        private async Task RestartAfterTokenChangeAsync()
        {
            if (!TokenFileWriter.HasAnyToken(AppDomain.CurrentDomain.BaseDirectory))
            {
                if (monitorService != null)
                {
                    monitorService.Dispose();
                    monitorService = null;
                }

                viewModel.StatusText = "未登录";
                viewModel.WarningText = "没有可用 token";
                viewModel.CurrentPosition = null;
                viewModel.Credential = null;
                viewModel.RoleBinding = null;
                viewModel.AvailableRoles = new List<RoleSession>();
                UpdateMainUi();
                return;
            }

            await StartMonitorAsync();
        }

        private void UpdateMainUi()
        {
            suppressControlEvents = true;
            CoordinateWindowCheckBox.IsChecked = viewModel.IsCoordinateWindowOpen;
            FollowGameCheckBox.IsChecked = viewModel.FollowGameWindow;
            RecordCaptureDataCheckBox.IsChecked = viewModel.RecordCaptureData;
            HotkeyText.Text = viewModel.Hotkey.ToString();
            CaptureHotkeyButton.Content = capturingHotkey ? "按一个键..." : "设置快捷键";
            ReconnectButton.IsEnabled = !isConnecting;
            TokenManagerButton.IsEnabled = !isConnecting;
            RoleComboBox.ItemsSource = viewModel.AvailableRoles;
            SelectRole(viewModel.SelectedRoleKey);
            SwitchRoleButton.IsEnabled = !isConnecting && RoleComboBox.SelectedItem != null;
            StartCaptureButton.IsEnabled = !isCapturing;
            StopCaptureButton.IsEnabled = isCapturing;
            ManualDetectZiplineButton.IsEnabled = viewModel.CurrentPosition != null && !isLoadingCaptureMarks;
            bool hasRealtimeResult = realtimeDetector != null && realtimeDetector.DetectedStops.Count > 0;
            CopyCaptureTextButton.IsEnabled = hasRealtimeResult;
            CopyCaptureJsonButton.IsEnabled = hasRealtimeResult;
            SelectFollowPosition(viewModel.FollowPosition);
            UpdateOffsetControls();
            suppressControlEvents = false;

            StatusText.Text = string.IsNullOrWhiteSpace(viewModel.StatusText) ? "正在连接..." : viewModel.StatusText;
            WarningText.Text = viewModel.WarningText ?? string.Empty;
            if (viewModel.CurrentPosition == null)
            {
                PositionText.Text = "X: -  Y: -  Z: -";
            }
            else
            {
                PositionText.Text = string.Format(
                    CultureInfo.InvariantCulture,
                    "X: {0}  Y: {1}  Z: {2}",
                    CoordinateFormatter.Format(viewModel.CurrentPosition.X),
                    CoordinateFormatter.Format(viewModel.CurrentPosition.Y),
                    CoordinateFormatter.Format(viewModel.CurrentPosition.Z));
            }

            ManualZiplineResultText.Text = viewModel.ZiplineResult == null
                ? ManualZiplineResultText.Text
                : viewModel.ZiplineResult.Found ? viewModel.ZiplineResult.ToTupleText() : viewModel.ZiplineResult.Message;

            UpdateCopyButtons();
            UpdateCaptureStatusText();
        }

        private void UpdateCaptureStatusText()
        {
            if (isCapturing)
            {
                CaptureStatusText.Text = string.Format(
                    CultureInfo.InvariantCulture,
                    viewModel.RecordCaptureData
                        ? "采集中：样本 {0}，目录 {1}。停在滑索上等待识别，也可以手动获取当前滑索。"
                        : "采集中：未记录文件。停在滑索上等待识别，也可以手动获取当前滑索。",
                    captureRecorder.SampleCount,
                    captureRecorder.CurrentSessionDirectory);
                RealtimeDetectionText.Text = realtimeDetector == null
                    ? "实时识别：等待滑索数据"
                    : "实时识别：" + realtimeDetector.DetectedStops.Count + " 个" + Environment.NewLine + realtimeDetector.GetResultText();
                return;
            }

            CaptureStatusText.Text = viewModel.RecordCaptureData
                ? "未采集。开始采集后会记录 positions、marks、detections 文件。"
                : "未采集。当前不会记录文件，只保留本次识别结果用于复制。";
            RealtimeDetectionText.Text = realtimeDetector == null ? string.Empty : realtimeDetector.GetResultText();
        }

        private void UpdateCopyButtons()
        {
            bool canCopy = viewModel.ZiplineResult != null && viewModel.ZiplineResult.Found;
            CopyManualTupleButton.IsEnabled = canCopy;
            CopyManualJsonButton.IsEnabled = canCopy;
        }

        private string GetSelectedFollowPosition()
        {
            var item = FollowPositionComboBox.SelectedItem as ComboBoxItem;
            return item == null ? "正上" : Convert.ToString(item.Content);
        }

        private void SelectFollowPosition(string value)
        {
            string target = string.IsNullOrWhiteSpace(value) ? "正上" : value;
            foreach (object item in FollowPositionComboBox.Items)
            {
                var comboBoxItem = item as ComboBoxItem;
                if (comboBoxItem != null && string.Equals(Convert.ToString(comboBoxItem.Content), target, StringComparison.Ordinal))
                {
                    FollowPositionComboBox.SelectedItem = comboBoxItem;
                    return;
                }
            }

            FollowPositionComboBox.SelectedIndex = 0;
        }

        private void UpdateOffsetControls()
        {
            FollowOffsetSettings offset = viewModel.GetFollowOffset(viewModel.FollowPosition);
            UpdateOffsetRanges(viewModel.FollowPosition);
            FollowHorizontalOffsetSlider.Value = offset.Horizontal;
            FollowVerticalOffsetSlider.Value = offset.Vertical;
            UpdateOffsetText();
        }

        private void UpdateOffsetRanges(string position)
        {
            const double maxOffset = 500;
            bool horizontalCanBeNegative = string.Equals(position, "正上", StringComparison.Ordinal)
                || string.Equals(position, "正下", StringComparison.Ordinal);
            bool verticalCanBeNegative = string.Equals(position, "正左", StringComparison.Ordinal)
                || string.Equals(position, "正右", StringComparison.Ordinal);

            FollowHorizontalOffsetSlider.Minimum = horizontalCanBeNegative ? -maxOffset : 0;
            FollowHorizontalOffsetSlider.Maximum = maxOffset;
            FollowVerticalOffsetSlider.Minimum = verticalCanBeNegative ? -maxOffset : 0;
            FollowVerticalOffsetSlider.Maximum = maxOffset;
        }

        private void UpdateOffsetText()
        {
            if (FollowHorizontalOffsetText == null || FollowVerticalOffsetText == null)
            {
                return;
            }

            FollowHorizontalOffsetText.Text = Math.Round(FollowHorizontalOffsetSlider.Value).ToString(CultureInfo.InvariantCulture);
            FollowVerticalOffsetText.Text = Math.Round(FollowVerticalOffsetSlider.Value).ToString(CultureInfo.InvariantCulture);
        }

        private void SelectRole(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                if (RoleComboBox.Items.Count > 0 && RoleComboBox.SelectedIndex < 0)
                {
                    RoleComboBox.SelectedIndex = 0;
                }

                return;
            }

            foreach (object item in RoleComboBox.Items)
            {
                RoleSession role = item as RoleSession;
                if (role != null && string.Equals(role.Key, key, StringComparison.Ordinal))
                {
                    RoleComboBox.SelectedItem = role;
                    return;
                }
            }
        }
    }
}
