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
    private bool _isRecording = false;

    public MainWindow()
    {
        InitializeComponent();

        // 实例化 AudioManager 并在音量改变时订阅事件
        _audioManager = new AudioManager();
        _audioManager.OnVolumeChanged += AudioManager_OnVolumeChanged;

        // 实例化 OBSService 并订阅连接状态
        _obsService = new OBSService();
        _obsService.ConnectionStateChanged += ObsService_ConnectionStateChanged;

        // 初始化时加载所有系统可用麦克风
        LoadMicrophones();
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
        _audioManager.StopRecording();
        _obsService.Disconnect();
        base.OnClosed(e);
    }
}