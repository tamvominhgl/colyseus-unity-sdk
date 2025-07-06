using UnityEngine;
using System;
using System.Collections.Generic;
using NativeWebSocket;
using System.Buffers;
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
            await SendMessage(System.Net.WebSockets.WebSocketMessageType.Binary, bytes);
        }

        public async Awaitable Send(ReadOnlySequence<byte> sequence)
        {
            if (sequence.IsSingleSegment)
            {
                await SendMessage(System.Net.WebSockets.WebSocketMessageType.Binary, sequence.First);
            }
            else
            {
                var enumerator = sequence.GetEnumerator();

                ReadOnlyMemory<byte> current = default;
                ReadOnlyMemory<byte> next = default;

                TryGetNextMemory(ref enumerator, ref current);
                while (true)
                {
                    bool hasNext = TryGetNextMemory(ref enumerator, ref next);
                    if (hasNext)
                    {
                        await SendMessage(System.Net.WebSockets.WebSocketMessageType.Binary, current, false);
                        current = next;
                    }
                    else
                    {
                        await SendMessage(System.Net.WebSockets.WebSocketMessageType.Binary, current, true);
                        break;
                    }
                }
            }
        }

        static bool TryGetNextMemory(ref ReadOnlySequence<byte>.Enumerator enumerator, ref ReadOnlyMemory<byte> memory)
        {
            while (memory.Length == 0)
            {
                if (!enumerator.MoveNext())
                {
                    return false;
                }

                memory = enumerator.Current;
            }

            return true;
        }
    }
}
