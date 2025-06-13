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

        public ConnectionState State => websocket?.State switch
        {
            Utilities.WebSockets.State.Connecting => ConnectionState.Connecting,
            Utilities.WebSockets.State.Open => ConnectionState.Open,
            Utilities.WebSockets.State.Closing => ConnectionState.Closing,
            _ => ConnectionState.Closed
        };

        readonly WebSocket websocket;
        bool disposed = false;

        public ColyseusConnection(string url, Dictionary<string, string> headers)
        {
            websocket = new(url, headers);

            websocket.OnOpen += OnSocketOpen;
            websocket.OnMessage += OnSocketMessage;
            websocket.OnError += OnSocketError;
            websocket.OnClose += OnSocketClose;
        }

        public Task Connect(CancellationToken cancellationToken = default)
        {
            return websocket.ConnectAsync(cancellationToken);
        }

        public Task Send(ArraySegment<byte> data, CancellationToken cancellationToken = default)
        {
            return websocket.SendAsync(data, cancellationToken);
        }

        public Task Close()
        {
            if (websocket.State == Utilities.WebSockets.State.Open)
            {
                return websocket.CloseAsync();
            }
            else
            {
                if (!disposed)
                {
                    disposed = true;
                    websocket.Dispose();
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
            if (!disposed)
            {
                disposed = true;
                websocket.Dispose();
            }
            OnClose((int)code);
        }
    }
}
