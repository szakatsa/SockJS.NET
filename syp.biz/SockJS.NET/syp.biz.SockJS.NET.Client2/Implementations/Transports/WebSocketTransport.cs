﻿using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using syp.biz.SockJS.NET.Client2.Interfaces;
using syp.biz.SockJS.NET.Common.Extensions;

namespace syp.biz.SockJS.NET.Client2.Implementations.Transports
{
    internal class WebSocketTransportFactory : ITransportFactory
    {
        #region Implementation of ITransportFactory
        public string Name => "websocket";
        public bool Enabled { get; set; } = CheckIfWebSocketIsSupported();
        public uint Priority { get; set; } = 100;
        public Task<ITransport> Build(ITransportConfiguration config)
        {
            config.Logger.Debug($"{nameof(this.Build)}: '{this.Name}' transport");
            var transport = new WebSocketTransport(config);
            return Task.FromResult<ITransport>(transport);
        }
        #endregion Implementation of ITransportFactory

        private static bool CheckIfWebSocketIsSupported()
        {
            try
            {
                using var socket = new ClientWebSocket();
                return true;
            }
            catch (PlatformNotSupportedException)
            {
                return false;
            }
            catch
            {
                throw;
            }
        }
    }

    internal class WebSocketTransport : ITransport
    {
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly ITransportConfiguration _config;
        private readonly ILogger _log;
        private readonly ClientWebSocket _socket;

        public WebSocketTransport(ITransportConfiguration config)
        {
            this._config = config;
            this._log = config.Logger;
            this._socket = new ClientWebSocket();
        }

        #region Implementation of IDisposable
        public void Dispose()
        {
            this._cts.Dispose();
            this._socket.Dispose();
        }
        #endregion Implementation of IDisposable

        #region Implementation of ITransport
        public event EventHandler<string>? Message;
        public event EventHandler? Connected;
        public event EventHandler? Disconnected;

        public string Name => "websocket";

        public async Task Connect(CancellationToken token)
        {
            var endpoint = this.BuildEndpoint();
            this._log.Info($"{nameof(this.Connect)}: {endpoint}");

            await this._socket.ConnectAsync(endpoint, token);
            this.Connected?.Invoke(this, EventArgs.Empty);
            _ = Task.Factory.StartNew(this.ReceiveLoop, this._cts, TaskCreationOptions.LongRunning).ConfigureAwait(false);
        }

        public Task Connect() => this.Connect(CancellationToken.None);

        public async Task Disconnect()
        {
            this._log.Info(nameof(this.Disconnect));
            if (this._socket.State == WebSocketState.Closed) return;
            await this._socket.CloseAsync(WebSocketCloseStatus.Empty, "", this._cts.Token);
        }

        public Task Send(string data) => this.Send(data, CancellationToken.None);

        public async Task Send(string data, CancellationToken token)
        {
            this._log.Debug($"{nameof(this.Send)}: {data}");
            if (data is null) return;

            this.VerifyOpen();
            var buffer = Encoding.UTF8.GetBytes(data).AsMemory();
            await this._socket.SendAsync(buffer, WebSocketMessageType.Text, true, token);
        }
        #endregion Implementation of ITransport

        private Uri BuildEndpoint()
        {
            var endpoint = new UriBuilder(this._config.BaseEndpoint);
            endpoint.Scheme = endpoint.Scheme == "https" ? "wss" : "ws";
            endpoint.AddPath("/websocket");
            return endpoint.Uri;
        }

        private async void ReceiveLoop(object obj)
        {
            try
            {
                this._log.Debug(nameof(this.ReceiveLoop));
                while (!this._cts.IsCancellationRequested && this._socket.State == WebSocketState.Open)
                {
                    var builder = new StringBuilder();
                    var buffer = new byte[1024];
                    var segment = new ArraySegment<byte>(buffer);
                    var result = await this._socket.ReceiveAsync(segment, this._cts.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        this._log.Error("Server sent close message");
                        await this.Disconnect();
                        break;
                    }
                    var data = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    builder.Append(data);
                    if (!result.EndOfMessage) continue;
                    var message = builder.ToString();
                    builder = new StringBuilder();

                    _ = Task.Run(() => this.Message?.Invoke(this, message)).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                this._log.Error($"{nameof(this.ReceiveLoop)}: {ex}");
            }
        }

        private void VerifyOpen()
        {
            var state = this._socket.State;
            if (state != WebSocketState.Open) throw new Exception($"Invalid socket state '{state}");
        }
    }
}
