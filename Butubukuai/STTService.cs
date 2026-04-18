using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Butubukuai
{
    public class WordTimestamp
    {
        public string Text { get; set; } = string.Empty;
        public int BeginTime { get; set; }
        public int EndTime { get; set; }
    }

    public class RecognizedTextEventArgs : EventArgs
    {
        public string Text { get; set; } = string.Empty;
        public int DurationMs { get; set; }
        public List<WordTimestamp> Words { get; set; } = new List<WordTimestamp>();
    }

    public class STTService
    {
        private ClientWebSocket? _webSocket;
        private CancellationTokenSource? _cts;
        private readonly string _endpoint = "wss://dashscope.aliyuncs.com/api-ws/v1/inference/";

        /// <summary>
        /// 当接收到识别文本时触发
        /// </summary>
        public event EventHandler<RecognizedTextEventArgs>? OnTextRecognized;
        public event EventHandler<string>? OnError;
        public event EventHandler? OnConnected;
        public event EventHandler? OnDisconnected;

        public bool IsConnected => _webSocket?.State == WebSocketState.Open;

        private string _currentApiKey = string.Empty;
        private bool _isManualDisconnect = false;

        /// <summary>
        /// 启动连接并发送配置握手帧
        /// </summary>
        public async Task ConnectAsync(string apiKey)
        {
            _isManualDisconnect = false;
            _currentApiKey = apiKey;
            DisconnectInternal(false);
            
            _webSocket = new ClientWebSocket();
            _cts = new CancellationTokenSource();

            // 阿里云 WS 身份验证要求 Header 带有 Authorization Bearer
            _webSocket.Options.SetRequestHeader("Authorization", $"Bearer {apiKey}");

            try
            {
                await _webSocket.ConnectAsync(new Uri(_endpoint), _cts.Token);
                
                // 握手成功后，首包必须是一个 JSON 配置帧 (强制要求 16000Hz, PCM)
                await SendInitialConfigFrameAsync();

                // 发出连接成功事件
                OnConnected?.Invoke(this, EventArgs.Empty);

                // 启动底层的后台接收环
                _ = ReceiveLoopAsync(_cts.Token);
            }
            catch (Exception ex)
            {
                throw new Exception($"STT WebSocket 连接失败: {ex.Message}", ex);
            }
        }

        public void Disconnect()
        {
            _isManualDisconnect = true;
            DisconnectInternal(true);
        }

        private void DisconnectInternal(bool wait)
        {
            try
            {
                if (_cts != null && !_cts.IsCancellationRequested)
                {
                    _cts.Cancel();
                }

                if (_webSocket != null && _webSocket.State == WebSocketState.Open)
                {
                    // 仅发出关闭请求即返回
                    var closeTask = _webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Client disconnecting", CancellationToken.None);
                    if (wait) closeTask.Wait(1000); 
                }
            }
            catch { }
            finally
            {
                _webSocket?.Dispose();
                _webSocket = null;
                _cts?.Dispose();
                _cts = null;
            }
        }

        private async Task SendInitialConfigFrameAsync()
        {
            // 这是阿里 DashScope paraformer-realtime-v2 的必要 JSON 帧格式
            var configData = new
            {
                header = new
                {
                    action = "run-task",
                    task_id = Guid.NewGuid().ToString("N"),
                    streaming = "duplex"
                },
                payload = new
                {
                    task_group = "audio",
                    task = "asr",
                    function = "recognition",
                    model = "paraformer-realtime-v2",
                    parameters = new
                    {
                        sample_rate = 16000,
                        format = "pcm",
                        enable_intermediate_result = true
                    },
                    input = new { } // 关键修复：显式包含空的 input 对象以符合协议
                }
            };

            string json = JsonSerializer.Serialize(configData);
            byte[] bytes = Encoding.UTF8.GetBytes(json);

            if (_webSocket != null && _webSocket.State == WebSocketState.Open && _cts != null)
            {
                await _webSocket.SendAsync(
                    new ArraySegment<byte>(bytes), 
                    WebSocketMessageType.Text, 
                    true, 
                    _cts.Token);
            }
        }

        /// <summary>
        /// 推送二进制音频流
        /// </summary>
        public async Task SendAudioDataAsync(byte[] pcmData)
        {
            if (_webSocket == null || _webSocket.State != WebSocketState.Open || _cts == null)
            {
                return;
            }

            try
            {
                System.Diagnostics.Debug.WriteLine($"[Audio Debug] 推送 PCM 数据，长度：{pcmData.Length} bytes");
                Console.WriteLine($"[Audio Debug] 推送 PCM 数据，长度：{pcmData.Length} bytes");
                
                await _webSocket.SendAsync(new ArraySegment<byte>(pcmData), WebSocketMessageType.Binary, true, _cts.Token);
            }
            catch (Exception ex)
            {
                OnError?.Invoke(this, $"发送 PCM 数据出错: {ex.Message}");
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            var buffer = new byte[8192];
            var sb = new StringBuilder(); // 支持长消息片段的拼合

            while (_webSocket != null && _webSocket.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                try
                {
                    var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        DisconnectInternal(false);
                        if (!_isManualDisconnect) 
                        {
                            OnDisconnected?.Invoke(this, EventArgs.Empty);
                            _ = AutoReconnectAsync();
                        }
                        break;
                    }

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        string chunk = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        sb.Append(chunk);

                        if (result.EndOfMessage)
                        {
                            string fullMessage = sb.ToString();
                            sb.Clear();
                            
                            System.Diagnostics.Debug.WriteLine($"[STT Raw] {fullMessage}");
                            Console.WriteLine($"[STT Raw] {fullMessage}");
                            
                            ProcessServerMessage(fullMessage);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    OnError?.Invoke(this, $"STT 接收循环断开: {ex.Message}");
                    if (!_isManualDisconnect) 
                    {
                        OnDisconnected?.Invoke(this, EventArgs.Empty);
                        _ = AutoReconnectAsync();
                    }
                    break;
                }
            }
        }

        private async Task AutoReconnectAsync()
        {
            while (!_isManualDisconnect)
            {
                await Task.Delay(3000);
                try
                {
                    await ConnectAsync(_currentApiKey);
                    break;
                }
                catch
                {
                    // 继续重试
                }
            }
        }

        private void ProcessServerMessage(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                string text = ExtractText(doc.RootElement);

                if (!string.IsNullOrEmpty(text))
                {
                    var eventArgs = new RecognizedTextEventArgs
                    {
                        Text = text,
                        DurationMs = 1000 // 强制赋予 FallbackDurationMs = 1000
                    };

                    OnTextRecognized?.Invoke(this, eventArgs);
                }
            }
            catch
            {
                // 忽略非标准的通知包 (例如只有 event 等心跳通知)
            }
        }

        /// <summary>
        /// 暴力展平搜索提取 text 字段
        /// </summary>
        private string ExtractText(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                // 优先查找当前层级的 text
                if (element.TryGetProperty("text", out var textProp) && textProp.ValueKind == JsonValueKind.String)
                {
                    string val = textProp.GetString() ?? "";
                    if (!string.IsNullOrEmpty(val)) return val;
                }
                
                foreach (var prop in element.EnumerateObject())
                {
                    string val = ExtractText(prop.Value);
                    if (!string.IsNullOrEmpty(val)) return val;
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    string val = ExtractText(item);
                    if (!string.IsNullOrEmpty(val)) return val;
                }
            }
            return string.Empty;
        }
    }
}
