using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Utilities.WebSockets;

namespace Colyseus
{
    public class ColyseusConnection
    {
        public bool IsOpen;

        public event Action OnOpen = delegate { };
        public event Action<byte[]> OnMessage = delegate { };
        public event Action<string> OnError = delegate { };
        public event Action<int> OnClose = delegate { };

        public State State => _socket.State;

        WebSocket _socket;
        bool _disposed = false;

        public ColyseusConnection(string url, Dictionary<string, string> headers)
        {
            _socket = new(url, headers);

            _socket.OnOpen += _OnOpen;
            _socket.OnMessage += _OnMessage;
            _socket.OnError += _OnError;
            _socket.OnClose += _OnClose;
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
            if (_socket.State == State.Open)
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

        protected void _OnOpen()
        {
            IsOpen = true;
            OnOpen();
        }

        protected void _OnMessage(DataFrame frame)
        {
            OnMessage(frame.Data.ToArray());
        }

        protected void _OnError(Exception ex)
        {
            OnError(ex.Message);
        }

        protected void _OnClose(CloseStatusCode code, string reason)
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
