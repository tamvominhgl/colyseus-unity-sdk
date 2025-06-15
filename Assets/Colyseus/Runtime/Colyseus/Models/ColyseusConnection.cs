using UnityEngine;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NativeWebSocket;
// ReSharper disable InconsistentNaming

namespace Colyseus
{
    /// <summary>
    ///     WebSocket connection representation with some custom functionality
    /// </summary>
    public class ColyseusConnection : WebSocket
    {
        public bool IsOpen => State == WebSocketState.Open;

        public ColyseusConnection(string url, Dictionary<string, string> headers) : base(url, headers)
        {

        }

        public async Task Send(ReadOnlyMemory<byte> bytes)
        {
            await SendMessage(System.Net.WebSockets.WebSocketMessageType.Binary, bytes);
        }
    }
}
