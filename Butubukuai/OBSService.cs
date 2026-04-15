using System;
using System.Threading.Tasks;
using OBSWebsocketDotNet;
using OBSWebsocketDotNet.Communication;

namespace Butubukuai
{
    /// <summary>
    /// OBS 服务类，负责与 OBS WebSocket 进行通信，实现高内聚
    /// </summary>
    public class OBSService
    {
        private readonly OBSWebsocket _obs;

        /// <summary>
        /// 当前是否已连接到 OBS
        /// </summary>
        public bool IsConnected => _obs.IsConnected;

        /// <summary>
        /// 当连接状态发生改变时触发
        /// </summary>
        public event EventHandler<bool>? ConnectionStateChanged;

        public OBSService()
        {
            _obs = new OBSWebsocket();
            // 订阅底层连接事件
            _obs.Connected += Obs_Connected;
            _obs.Disconnected += Obs_Disconnected;
        }

        private void Obs_Connected(object? sender, EventArgs e)
        {
            ConnectionStateChanged?.Invoke(this, true);
        }

        private void Obs_Disconnected(object? sender, ObsDisconnectionInfo e)
        {
            ConnectionStateChanged?.Invoke(this, false);
        }

        /// <summary>
        /// 异步连接到 OBS WebSocket
        /// </summary>
        /// <param name="ip">IP 地址</param>
        /// <param name="port">端口号</param>
        /// <param name="password">密码</param>
        public async Task ConnectAsync(string ip, int port, string password)
        {
            string url = $"ws://{ip}:{port}";

            // 使用 Task.Run 包装以防止阻塞 UI 线程
            // obs-websocket-dotnet v5 兼容连接方法
            await Task.Run(() =>
            {
                _obs.ConnectAsync(url, password);
                
                // 等待连接状态改变，增加一个简单的超时或等待机制，避免直接返回
                int timeoutMs = 3000;
                int waited = 0;
                while (!_obs.IsConnected && waited < timeoutMs)
                {
                    Task.Delay(100).Wait();
                    waited += 100;
                }
                
                if (!_obs.IsConnected)
                {
                    // 若等待后仍未连接，通常表示由于密码错误或目标不存在而失败
                    throw new Exception("连接超时或握手失败，请检查 IP、端口和密码是否正确，以及 OBS 是否已启动 WebSocket 插件。");
                }
            });
        }

        /// <summary>
        /// 断开与 OBS 的连接
        /// </summary>
        public void Disconnect()
        {
            if (_obs.IsConnected)
            {
                _obs.Disconnect();
            }
        }

        /// <summary>
        /// 获取所有的输入源名称
        /// </summary>
        /// <returns>输入源名称列表</returns>
        public List<string> GetAudioInputNames()
        {
            var names = new List<string>();
            if (!_obs.IsConnected) return names;

            try
            {
                // 获取当前场景下的所有可用输入源
                var inputs = _obs.GetInputList();
                foreach (var input in inputs)
                {
                    names.Add(input.InputName);
                }
            }
            catch
            {
                // 获取失败时不抛出阻断异常，返回空/部分列表即可
            }

            return names;
        }

        /// <summary>
        /// 设置指定音频源的静音状态
        /// </summary>
        /// <param name="sourceName">OBS 中的音轨名称</param>
        /// <param name="isMute">是否静音</param>
        public void SetSourceMute(string sourceName, bool isMute)
        {
            if (!_obs.IsConnected)
            {
                throw new InvalidOperationException("未连接到 OBS，无法执行静音操作。");
            }

            // OBS WebSocket v5 使用 SetInputMute 方法控制音轨状态
            _obs.SetInputMute(sourceName, isMute);
        }
    }
}
