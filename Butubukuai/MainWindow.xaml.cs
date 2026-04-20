using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Diagnostics;
using System.Windows.Navigation;
using NAudio.Wave;
using System.IO;

namespace Butubukuai;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly AudioManager _audioManager;
    private readonly OBSService _obsService;
    private readonly FilterEngine _filterEngine;
    private readonly STTService _sttService;
    private AppConfig _config = new AppConfig();
    private bool _isRecording = false;
    private System.Threading.CancellationTokenSource? _unmuteCts;

    // 校准舱专用变量
    private bool _isCalibrationRecording = false;
    private bool _isCalibrationIntercepting = false;
    private WaveFileWriter? _calibrationWriter;
    private Stopwatch _calibrationStopwatch = new Stopwatch();
    private long _recordedTriggerTimeMs = 0;

    public MainWindow()
    {
        InitializeComponent();

        // 1. 初始化音频引擎
        _audioManager = new AudioManager();
        _audioManager.OnVolumeChanged += AudioManager_OnVolumeChanged;
        _audioManager.OnAudioDataAvailable += AudioManager_OnAudioDataAvailable;

        // 2. 初始化 OBS 服务
        _obsService = new OBSService();
        _obsService.ConnectionStateChanged += ObsService_ConnectionStateChanged;

        // 3. 初始化过滤及 STT AI 引擎
        _filterEngine = new FilterEngine();
        _sttService = new STTService();
        _sttService.OnTextRecognized += SttService_OnTextRecognized;
        _sttService.OnError += SttService_OnError;
        _sttService.OnDisconnected += SttService_OnDisconnected;
        _sttService.OnConnected += SttService_OnConnected;

        // 4. 加载配置和下拉框信息
        LoadConfigData();
        LoadMicrophones();
    }

    /// <summary>
    /// 加载配置文件内容到 UI 并推送到引擎
    /// </summary>
    private void LoadConfigData()
    {
        _config = ConfigManager.Load();
        ApiKeyTextBox.Text = _config.ApiKey;
        
        ObsIpTextBox.Text = _config.ObsIpAddress;
        ObsPortTextBox.Text = _config.ObsPort.ToString();
        ObsPasswordBox.Password = _config.ObsPassword;
        ObsMediaSourceNameTextBox.Text = _config.ObsMediaSourceName;
        ObsSyncDelayTextBox.Text = _config.ObsSyncDelay.ToString();
        FineTuneOffsetSlider.Value = _config.FineTuneOffset;

        // 绑定到 DataGrid
        RuleGroupsDataGrid.ItemsSource = _config.RuleGroups;

        // 初始化词库
        _filterEngine.LoadWords(_config.RuleGroups);
    }

    /// <summary>
    /// 加载麦克风列表绑定到下拉框
    /// </summary>
    private void LoadMicrophones()
    {
        var mics = _audioManager.GetMicrophoneList();
        var micItems = new List<KeyValuePair<string, int>>();

        for (int i = 0; i < mics.Count; i++)
        {
            micItems.Add(new KeyValuePair<string, int>(mics[i], i));
        }

        MicComboBox.ItemsSource = micItems;
        if (micItems.Count > 0)
        {
            MicComboBox.SelectedIndex = 0;
        }
        else
        {
            MessageBox.Show("未检测到可用的麦克风输入设备。", "提示信息");
        }
    }

    /// <summary>
    /// 保存配置并应用
    /// </summary>
    private void SaveConfigButton_Click(object sender, RoutedEventArgs e)
    {
        _config.ApiKey = ApiKeyTextBox.Text.Trim();
        _config.ObsIpAddress = ObsIpTextBox.Text.Trim();
        if (int.TryParse(ObsPortTextBox.Text.Trim(), out int port)) _config.ObsPort = port;
        _config.ObsPassword = ObsPasswordBox.Password;
        _config.ObsMediaSourceName = ObsMediaSourceNameTextBox.Text.Trim();
        if (int.TryParse(ObsSyncDelayTextBox.Text.Trim(), out int syncDelay)) _config.ObsSyncDelay = syncDelay;
        _config.FineTuneOffset = (int)FineTuneOffsetSlider.Value;

        // 绑定机制会自动更新 RuleGroups，我们直接将内存状态存回本地并刷新底层引擎
        ConfigManager.Save(_config);
        _filterEngine.LoadWords(_config.RuleGroups);

        MessageBox.Show($"配置已保存并热重载引擎。\n当前总词池实际展开：{_filterEngine.BannedWordCount} 个阻断词。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>
    /// 手动添加一行新规则
    /// </summary>
    private void AddRowButton_Click(object sender, RoutedEventArgs e)
    {
        var newItem = new RuleGroup { GroupName = "新规则组", Words = "靠,他妈,#城市#", SoundPath = "" };
        _config.RuleGroups.Add(newItem);
        if (_config.RuleGroups.Count > 0)
        {
            RuleGroupsDataGrid.ScrollIntoView(newItem);
        }
    }

    /// <summary>
    /// 处理表格中选取专属媒体音文件的操作
    /// </summary>
    private void BrowseSoundButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is RuleGroup item)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Audio Files|*.wav;*.mp3|All Files|*.*",
                Title = "选择专属替换音"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                item.SoundPath = openFileDialog.FileName;
                // 注意：不再调用 Refresh()！因为后台的 RuleGroup 已实现 INotifyPropertyChanged，UI将自动刷新
            }
        }
    }

    /// <summary>
    /// 测试验证 STT WebSocket 连接
    /// </summary>
    private async void TestSttConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_sttService.IsConnected)
        {
            _sttService.Disconnect();
            SttStatusTextBlock.Text = "已断开";
            SttStatusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(244, 67, 54)); // Red
            TestSttConnectButton.Content = "[第 3 步] 连通 AI 大脑";
            TestSttConnectButton.Background = new SolidColorBrush(Color.FromRgb(156, 39, 176)); // #9C27B0
            return;
        }

        string apiKey = ApiKeyTextBox.Text.Trim();
        if (string.IsNullOrEmpty(apiKey))
        {
            MessageBox.Show("请先填写阿里云 DashScope API Key", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        TestSttConnectButton.IsEnabled = false;
        SttStatusTextBlock.Text = "连接中...";
        SttStatusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(255, 152, 0)); // Orange

        try
        {
            await _sttService.ConnectAsync(apiKey);
            // OnConnected 事件将接管 UI 的成功状态变更
        }
        catch (Exception ex)
        {
            SttStatusTextBlock.Text = "未连接";
            SttStatusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(244, 67, 54)); // Red
            MessageBox.Show($"STT 连接测试失败：\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            TestSttConnectButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// 开始/停止 音频采集
    /// </summary>
    private void ToggleRecordButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isRecording)
        {
            if (MicComboBox.SelectedValue is int deviceNumber)
            {
                _audioManager.StartRecording(deviceNumber);
                _isRecording = true;
                
                ToggleRecordButton.Content = "[第 1 步] 停止采集";
                ToggleRecordButton.Background = new SolidColorBrush(Color.FromRgb(244, 67, 54)); // Red
                MicComboBox.IsEnabled = false;
            }
            else
            {
                MessageBox.Show("请先选择一个麦克风设备。", "提示信息");
            }
        }
        else
        {
            _audioManager.StopRecording();
            _isRecording = false;

            ToggleRecordButton.Content = "[第 1 步] 开启麦克风监听";
            ToggleRecordButton.Background = new SolidColorBrush(Color.FromRgb(33, 150, 243)); // Blue
            MicComboBox.IsEnabled = true;
        }
    }

    /// <summary>
    /// 拿到音频原生的流对象，立即向 STT Websocket 转发
    /// </summary>
    private void AudioManager_OnAudioDataAvailable(object? sender, byte[] pcmData)
    {
        if (_sttService.IsConnected)
        {
            // 通过后台发送，避免稍微堆积卡死UI或拉流
            _ = _sttService.SendAudioDataAsync(pcmData);
        }

        if (_isCalibrationRecording && _calibrationWriter != null)
        {
            try
            {
                _calibrationWriter.Write(pcmData, 0, pcmData.Length);
            }
            catch { }
        }
    }

    /// <summary>
    /// 当识别出现文字的回调
    /// </summary>
    private void SttService_OnTextRecognized(object? sender, RecognizedTextEventArgs e)
    {
        string text = e.Text;
        System.Diagnostics.Debug.WriteLine($"[UI Debug] 准备上屏文字：{text}");
        Console.WriteLine($"[UI Debug] 准备上屏文字：{text}");

        // 返回主线程以更新 UI 和触发联动
        Dispatcher.InvokeAsync(() =>
        {
            // 用时间戳标记并回显输出
            string log = $"[{DateTime.Now:HH:mm:ss}] {text}\n";
            RecognizedResultTextBox.Text = log + RecognizedResultTextBox.Text;

            // 核心逻辑：触发判定
            var (isMatch, durationMs, soundPath) = _filterEngine.CheckAndGetDuration(e);
            if (isMatch)
            {
                TriggerCensorship(text, durationMs, soundPath);
            }
        });
    }

    private void SttService_OnError(object? sender, string errorMessage)
    {
        Dispatcher.InvokeAsync(() =>
        {
            string log = $"[{DateTime.Now:HH:mm:ss}] [异常] {errorMessage}\n";
            RecognizedResultTextBox.Text = log + RecognizedResultTextBox.Text;
        });
    }

    private async void SttService_OnDisconnected(object? sender, EventArgs e)
    {
        await Dispatcher.InvokeAsync(async () =>
        {
            SttStatusTextBlock.Text = "断线重连中...";
            SttStatusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(244, 67, 54)); // Red
            TestSttConnectButton.Content = "[第 3 步] 异常断线拉响警报";

            for (int i = 0; i < 3; i++)
            {
                System.Media.SystemSounds.Exclamation.Play();
                await Task.Delay(500);
            }
        });
    }

    private void SttService_OnConnected(object? sender, EventArgs e)
    {
        Dispatcher.InvokeAsync(() =>
        {
            SttStatusTextBlock.Text = "已连接并鉴权成功 (待命中)";
            SttStatusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Green
            TestSttConnectButton.Content = "[第 3 步] 断开 AI 大脑";
            TestSttConnectButton.Background = new SolidColorBrush(Color.FromRgb(244, 67, 54)); // Red
        });
    }

    /// <summary>
    /// 触发阻断和发声 (带防抖的自动解封)
    /// </summary>
    private async void TriggerCensorship(string matchedText, int durationMs, string soundPath)
    {
        if (_isCalibrationIntercepting)
        {
            if (_recordedTriggerTimeMs == 0)
            {
                _recordedTriggerTimeMs = _calibrationStopwatch.ElapsedMilliseconds;
            }
            return; // 拦截触发，不操作 OBS
        }

        string? sourceName = ObsSourceComboBox.SelectedItem as string;
        if (string.IsNullOrEmpty(sourceName) || !_obsService.IsConnected) return;

        try
        {
            // 1. 取消上一次正在计时的恢复任务 (防抖核心)
            _unmuteCts?.Cancel();
            _unmuteCts = new System.Threading.CancellationTokenSource();
            var token = _unmuteCts.Token;

            int syncDelay = 2000;
            int fineTune = 0;
            Application.Current.Dispatcher.Invoke(() => 
            {
                if (int.TryParse(ObsSyncDelayTextBox.Text.Trim(), out int val)) syncDelay = val;
                fineTune = (int)FineTuneOffsetSlider.Value;
            });

            // 2. 狙击手延迟算法：真机耳返计算法
            int waitTime = syncDelay - 600 + fineTune;
            if (waitTime < 0) waitTime = 0;

            if (waitTime > 0)
            {
                await Task.Delay(waitTime, token);
            }

            // 3. 闭麦！实施静音屏蔽
            _obsService.SetSourceMute(sourceName, true);
            
            // 播放双轨美化音！
            if (!string.IsNullOrEmpty(soundPath))
            {
                _obsService.PlayMediaSource(_config.ObsMediaSourceName, soundPath);
            }

            // 4. 异步倒计时动态计算出的毫秒数，保持闭麦度过违禁词时长
            await Task.Delay(durationMs, token);

            // 5. 等待结束且没有被新的违禁词打断，则完全解封
            if (!token.IsCancellationRequested)
            {
                _obsService.SetSourceMute(sourceName, false);
            }
        }
        catch (TaskCanceledException)
        {
            // 防抖中断任务导致的常规异常，做安静忽略
        }
        catch (Exception ex)
        {
            RecognizedResultTextBox.Text = $"[警告] 操作 OBS 发生异常: {ex.Message}\n" + RecognizedResultTextBox.Text;
        }
    }

    /// <summary>
    /// 处理音量变化并通过 Dispatcher 更新到 UI 上
    /// </summary>
    private void AudioManager_OnVolumeChanged(object? sender, float volume)
    {
        Dispatcher.InvokeAsync(() =>
        {
            VolumeProgressBar.Value = volume;
        });
    }

    /// <summary>
    /// 处理 OBS 连接状态事件，安全更新 UI 颜色及文案
    /// </summary>
    private void ObsService_ConnectionStateChanged(object? sender, bool isConnected)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (isConnected)
            {
                ObsStatusTextBlock.Text = "已连接";
                ObsStatusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Green
                ConnectObsButton.Content = "断开连接";
                ConnectObsButton.Background = new SolidColorBrush(Color.FromRgb(244, 67, 54)); // Red

                // 获取 OBS 中的音频源列表
                var inputNames = _obsService.GetAudioInputNames();
                ObsSourceComboBox.ItemsSource = inputNames;
                if (inputNames.Count > 0)
                {
                    ObsSourceComboBox.SelectedIndex = 0;
                }
            }
            else
            {
                ObsStatusTextBlock.Text = "未连接";
                ObsStatusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(244, 67, 54)); // Red
                ConnectObsButton.Content = "连接 OBS";
                ConnectObsButton.Background = new SolidColorBrush(Color.FromRgb(255, 152, 0)); // Orange
                
                // 断开连接后清空列表
                ObsSourceComboBox.ItemsSource = null;
            }
            
            ConnectObsButton.IsEnabled = true;
        });
    }

    /// <summary>
    /// 异步连接/断开 OBS
    /// </summary>
    private async void ConnectObsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_obsService.IsConnected)
        {
            _obsService.Disconnect();
        }
        else
        {
            string ip = ObsIpTextBox.Text.Trim();
            string password = ObsPasswordBox.Password;
            
            if (!int.TryParse(ObsPortTextBox.Text.Trim(), out int port))
            {
                MessageBox.Show("端口号格式不正确，请输入有效的整数。", "错误提示", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            ConnectObsButton.IsEnabled = false; // 禁用按钮防止重复点击
            ConnectObsButton.Content = "连接中...";

            try
            {
                await _obsService.ConnectAsync(ip, port, password);
                // 连接成功会在 ConnectionStateChanged 事件中更新 UI
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法连接到 OBS：\n{ex.Message}", "连接失败", MessageBoxButton.OK, MessageBoxImage.Error);
                
                // 恢复按钮状态
                ConnectObsButton.IsEnabled = true;
                ConnectObsButton.Content = "连接 OBS";
            }
        }
    }

    private async void RecordTestAudio_Click(object sender, RoutedEventArgs e)
    {
        if (!_isRecording)
        {
            MessageBox.Show("请先在第一步开启麦克风监听！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        RecordTestAudioButton.IsEnabled = false;
        PlayVerifyAudioButton.IsEnabled = false;
        RecordTestAudioButton.Content = "录音中...";
        _recordedTriggerTimeMs = 0;

        try
        {
            _calibrationWriter = new WaveFileWriter("temp_test.wav", new WaveFormat(16000, 16, 1));
            _isCalibrationRecording = true;
            _isCalibrationIntercepting = true;
            _calibrationStopwatch.Restart();

            await Task.Delay(5000);

            _isCalibrationRecording = false;
            _calibrationWriter?.Dispose();
            _calibrationWriter = null;
            
            RecordTestAudioButton.Content = "分析中...";
            
            // 额外等待 API 后置返回
            await Task.Delay(1500);

            if (_recordedTriggerTimeMs > 0)
            {
                PlayVerifyAudioButton.IsEnabled = true;
                RecordTestAudioButton.Content = "🔴 重新录制";
                MessageBox.Show("录制成功并捕获到违禁词！可以点击播放验证了。", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                RecordTestAudioButton.Content = "🔴 录音未捕获违禁词";
                MessageBox.Show("未在刚才的录音中检测到违禁词，或者断网迟滞。请重试。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"录制出错: {ex.Message}");
            RecordTestAudioButton.Content = "🔴 录制 5 秒测试音";
        }
        finally
        {
            _isCalibrationIntercepting = false;
            _calibrationStopwatch.Stop();
            RecordTestAudioButton.IsEnabled = true;
            if (_recordedTriggerTimeMs == 0) RecordTestAudioButton.Content = "🔴 录制 5 秒测试音";
        }
    }

    private async void PlayVerifyAudio_Click(object sender, RoutedEventArgs e)
    {
        if (!File.Exists("temp_test.wav")) return;

        PlayVerifyAudioButton.IsEnabled = false;
        try
        {
            using var reader = new AudioFileReader("temp_test.wav");
            using var waveOut = new WaveOutEvent();
            waveOut.Init(reader);
            waveOut.Play();

            int fineTune = 0;
            if (Application.Current != null)
            {
                fineTune = (int)FineTuneOffsetSlider.Value;
            }

            // 倒计时核心：记录的耗时 (也就是 T_trigger) - 预估API(600) + 微调
            int beepWait = (int)_recordedTriggerTimeMs - 600 + fineTune;
            if (beepWait < 0) beepWait = 0;

            if (beepWait > 0)
            {
                // 等待到该 Beep 的时刻
                await Task.Delay(beepWait);
            }

            // 发出 Beep 声
            System.Media.SystemSounds.Beep.Play();

            // 等待音频播完 (保护防抖)
            while (waveOut.PlaybackState == PlaybackState.Playing)
            {
                await Task.Delay(100);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"播放出错: {ex.Message}");
        }
        finally
        {
            PlayVerifyAudioButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// 窗口关闭时释放资源
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        _sttService.Disconnect();
        _audioManager.StopRecording();
        _obsService.Disconnect();
        base.OnClosed(e);
    }

    /// <summary>
    /// 打开/关闭 帮助抽屉面板
    /// </summary>
    private void HelpButton_Click(object sender, RoutedEventArgs e)
    {
        HelpFlyout.Visibility = HelpFlyout.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// 处理富文本中的超链接点击，在系统默认浏览器中打开
    /// </summary>
    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"无法打开链接：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 点击查看帮助大图
    /// </summary>
    private void ShowLargeImage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string imagePath)
        {
            try
            {
                LargeImageControl.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(imagePath));
                LargeImageViewer.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                MessageBox.Show("加载图片失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    /// <summary>
    /// 点击关闭大图
    /// </summary>
    private void CloseLargeImageButton_Click(object sender, RoutedEventArgs e)
    {
        LargeImageViewer.Visibility = Visibility.Collapsed;
        LargeImageControl.Source = null;
    }

    /// <summary>
    /// 点击背景遮罩关闭大图
    /// </summary>
    private void LargeImageViewer_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        LargeImageViewer.Visibility = Visibility.Collapsed;
        LargeImageControl.Source = null;
    }
}