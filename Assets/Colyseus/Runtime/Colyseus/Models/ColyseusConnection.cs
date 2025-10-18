using UnityEngine;
using System;
using System.Collections.Generic;
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

        public async Awaitable Send(ReadOnlyMemory<byte> bytes)
        {
            await SendMessage(bytes);
        }

        internal async Awaitable Send(SequencePool.Rental rental)
        {
            await SendMessage(rental);
        }
    }
}
