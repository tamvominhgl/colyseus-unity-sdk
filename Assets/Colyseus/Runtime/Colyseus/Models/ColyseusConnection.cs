using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Utilities.WebSockets;

namespace Colyseus
{
    public enum ConnectionState : ushort
    {
        Connecting = 0,
        Open = 1,
        Closing = 2,
        Closed = 3
    }

    public class ColyseusConnection
    {
        public bool IsOpen;

        public event Action OnOpen = delegate { };
        public event Action<byte[]> OnMessage = delegate { };
        public event Action<string> OnError = delegate { };
        public event Action<int> OnClose = delegate { };

        public ConnectionState State => (ConnectionState)_socket.State;

        WebSocket _socket;
        bool _disposed = false;

        public ColyseusConnection(string url, Dictionary<string, string> headers)
        {
            _socket = new(url, headers);

            _socket.OnOpen += OnSocketOpen;
            _socket.OnMessage += OnSocketMessage;
            _socket.OnError += OnSocketError;
            _socket.OnClose += OnSocketClose;
        }

        public Task Connect(CancellationToken cancellationToken = default)
        {
            return _socket.ConnectAsync(cancellationToken);
        }

        public Task Send(ArraySegment<byte> data, CancellationToken cancellationToken = default)
        {
            return _socket.SendAsync(data, cancellationToken);
        }

        public Task Close()
        {
            if (_socket.State == Utilities.WebSockets.State.Open)
            {
                return _socket.CloseAsync();
            }
            else
            {
                if (!_disposed)
                {
                    _disposed = true;
                    _socket.Dispose();
                }
                return Task.CompletedTask;
            }
        }

        protected void OnSocketOpen()
        {
            IsOpen = true;
            OnOpen();
        }

        protected void OnSocketMessage(DataFrame frame)
        {
            OnMessage(frame.Data.ToArray());
        }

        protected void OnSocketError(Exception ex)
        {
            OnError(ex.Message);
        }

        protected void OnSocketClose(CloseStatusCode code, string reason)
        {
            IsOpen = false;
            if (!_disposed)
            {
                _disposed = true;
                _socket.Dispose();
            }
            OnClose((int)code);
        }
    }
}
