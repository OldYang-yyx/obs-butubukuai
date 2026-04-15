using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

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
        AppIdTextBox.Text = _config.AppId;
        BannedWordsTextBox.Text = _config.BannedWords;

        // 初始化词库
        _filterEngine.LoadWords(_config.BannedWords);
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
        _config.AppId = AppIdTextBox.Text.Trim();
        _config.BannedWords = BannedWordsTextBox.Text.Trim();

        ConfigManager.Save(_config);
        _filterEngine.LoadWords(_config.BannedWords);

        MessageBox.Show($"配置已保存并热重载引擎。\n当前已加载 {_filterEngine.BannedWordCount} 个违禁词。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>
    /// 测试验证 STT WebSocket 连接
    /// </summary>
    private async void TestSttConnectButton_Click(object sender, RoutedEventArgs e)
    {
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
            SttStatusTextBlock.Text = "已连接并鉴权成功 (待命中)";
            SttStatusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Green
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
                
                ToggleRecordButton.Content = "停止采集";
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

            ToggleRecordButton.Content = "开始采集";
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
    }

    /// <summary>
    /// 当识别出现文字的回调
    /// </summary>
    private void SttService_OnTextRecognized(object? sender, string text)
    {
        System.Diagnostics.Debug.WriteLine($"[UI Debug] 准备上屏文字：{text}");
        Console.WriteLine($"[UI Debug] 准备上屏文字：{text}");

        // 返回主线程以更新 UI 和触发联动
        Dispatcher.InvokeAsync(() =>
        {
            // 用时间戳标记并回显输出
            string log = $"[{DateTime.Now:HH:mm:ss}] {text}\n";
            RecognizedResultTextBox.Text = log + RecognizedResultTextBox.Text;

            // 核心逻辑：触发判定
            if (_filterEngine.Check(text))
            {
                TriggerCensorship(text);
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

    /// <summary>
    /// 触发阻断和发声 (带防抖的自动解封)
    /// </summary>
    private async void TriggerCensorship(string matchedText)
    {
        System.Media.SystemSounds.Beep.Play();

        string? sourceName = ObsSourceComboBox.SelectedItem as string;
        if (string.IsNullOrEmpty(sourceName) || !_obsService.IsConnected) return;

        try
        {
            // 1. 取消上一次正在计时的恢复任务 (防抖核心)
            _unmuteCts?.Cancel();
            _unmuteCts = new System.Threading.CancellationTokenSource();
            var token = _unmuteCts.Token;

            // 2. 立即实施静音屏蔽
            _obsService.SetSourceMute(sourceName, true);
            TestMuteToggleButton.IsChecked = true;
            TestMuteToggleButton.Content = "已自动静音屏蔽中...";

            // 3. 异步倒计时 2000 毫秒
            await Task.Delay(2000, token);

            // 4. 等待结束且没有被新的违禁词打断，则完全解封
            if (!token.IsCancellationRequested)
            {
                _obsService.SetSourceMute(sourceName, false);
                TestMuteToggleButton.IsChecked = false;
                TestMuteToggleButton.Content = "测试静音";
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
                
                // 断开连接后清空列表并自动复位测试按钮状态
                ObsSourceComboBox.ItemsSource = null;
                TestMuteToggleButton.IsChecked = false;
                TestMuteToggleButton.Content = "测试静音";
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

    /// <summary>
    /// 测试静音按钮点击事件，调用 OBS 服务改变状态
    /// </summary>
    private void TestMuteToggleButton_Click(object sender, RoutedEventArgs e)
    {
        string? sourceName = ObsSourceComboBox.SelectedItem as string;
        if (string.IsNullOrEmpty(sourceName))
        {
            TestMuteToggleButton.IsChecked = false;
            MessageBox.Show("未识别到有效的 OBS 输入源，请连接 OBS 并从下拉框选择一个测试音轨。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!_obsService.IsConnected)
        {
            TestMuteToggleButton.IsChecked = false;
            MessageBox.Show("暂未连接到 OBS，请先进行连接再测试。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        bool isMuted = TestMuteToggleButton.IsChecked == true;
        try
        {
            _obsService.SetSourceMute(sourceName, isMuted);
            TestMuteToggleButton.Content = isMuted ? "已处于静音" : "测试静音";
        }
        catch (Exception ex)
        {
            TestMuteToggleButton.IsChecked = !isMuted; // 恢复之前的选中状态
            MessageBox.Show($"操作 OBS 失败：{ex.Message}\n请检查“{sourceName}”是否存在于 OBS 场景/音频混合器中。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
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
}