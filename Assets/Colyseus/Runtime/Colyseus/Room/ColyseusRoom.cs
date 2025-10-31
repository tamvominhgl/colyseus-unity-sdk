using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Colyseus.Schema;
using NativeWebSocket;
using UnityEngine;
using System.Buffers;
using System.Collections.Concurrent;

#if USE_MESSAGEPACK_CSHARP
using MessagePack;
#else
using GameDevWare.Serialization;
#endif

namespace Colyseus
{
    using Decode = Schema.Utils.Decode;
    using Encode = Schema.Utils.Encode;

    /// <summary>
    ///     Delegate function for when the <see cref="ColyseusClient" /> successfully connects to the
    ///     <see cref="ColyseusRoom{T}" />.
    /// </summary>
    public delegate void ColyseusOpenEventHandler();

    /// <summary>
    ///     Delegate function for when <see cref="ColyseusClient" /> leaves this room.
    /// </summary>
    /// <param name="code">Reason for closure</param>
    public delegate void ColyseusCloseEventHandler(int code);

    /// <summary>
    ///     Delegate function for when some error has been triggered in the room.
    /// </summary>
    /// <param name="code">Error code</param>
    /// <param name="message">Error message</param>
    public delegate void ColyseusErrorEventHandler(int code, string message);

    /// <summary>
    ///     Interface for functions expected of any <see cref="ColyseusRoom{T}"></see>.
    /// </summary>
    public interface IColyseusRoom
    {
        event ColyseusCloseEventHandler OnLeave;

        /// <summary>
        ///     Connection task
        /// </summary>
        /// <returns>Task that completes upon connection (or failure to connect)</returns>
        Awaitable Connect();

        /// <summary>
        ///     Disconnection task
        /// </summary>
        /// <param name="consented">True if by user's choice, false otherwise</param>
        /// <returns>Task that completes upon Leaving</returns>
        Awaitable Leave(bool consented);
    }

    [Serializable]
    public class ReconnectionToken
    {
        public string RoomId;
        public string Token;
    }

    internal readonly struct HandlerKey
    {
        private readonly ReadOnlySequence<byte> sequence;

        public HandlerKey(string type)
        {
            var bytes = new byte[type.Length * 2 + 6];
            var initialLen = Encode.setInitialBytes(ColyseusProtocol.ROOM_DATA, type, bytes);
            sequence = new ReadOnlySequence<byte>(bytes, 0, initialLen);
        }

        public HandlerKey(ReadOnlySequence<byte> sequence)
        {
            this.sequence = sequence;
        }

        internal class Comparer : IEqualityComparer<HandlerKey> {
            public bool Equals(HandlerKey x, HandlerKey y)
            {
                if (x.sequence.Length != y.sequence.Length)
                {
                    return false;
                }

                return x.sequence.SequenceEqual(y.sequence);
            }

            public int GetHashCode(HandlerKey obj)
            {
                return (int)obj.sequence.Length;
            }
        }
    }

    internal readonly struct HandlerValue
    {
        public readonly string Type;
        public readonly IColyseusMessageHandler Handler;

        public HandlerValue(string type, IColyseusMessageHandler handler)
        {
            Type = type;
            Handler = handler;
        }
    }

    public class ColyseusRoom<T> : IColyseusRoom where T : Schema.Schema
    {

        /// <summary>
        ///     Delegate for handling messages
        /// </summary>
        /// <remarks>Currently unused</remarks>
        /// <param name="message">Message data received</param>
        public delegate void RoomOnMessageEventHandler(object message);

        /// <summary>
        ///     Delegate for room state changes
        /// </summary>
        /// <param name="state">The state change received</param>
        /// <param name="isFirstState">Flag if first state received</param>
        public delegate void RoomOnStateChangeEventHandler(T state, bool isFirstState);

        /// <summary>
        ///     Reference to the room's WebSocket Connection
        /// </summary>
        public ColyseusConnection Connection;

        /// <summary>
        ///     Room ID
        /// </summary>
        public string RoomId;

        /// <summary>
        ///     Room name
        /// </summary>
        public string Name;

        /// <summary>
        ///     Dictionary of the message handlers that have been provided to the room
        /// </summary>
        protected ConcurrentDictionary<string, IColyseusMessageHandler> OnMessageHandlers = new();
        protected ConcurrentDictionary<byte, IColyseusMessageHandler> OnMessageByteHandlers = new();

        private readonly ConcurrentDictionary<HandlerKey, HandlerValue> OnMessageStringHandlers = new(new HandlerKey.Comparer());

        /// <summary>
        ///     Reference to the Serializer this room uses, determined and then generated based on the <see cref="SerializerId" />
        /// </summary>
        internal IColyseusSerializer<T> Serializer;

        /// <summary>
        ///     ID to determine which kind of serializer this room uses (<see cref="ColyseusSchemaSerializer{T}" /> or
        ///     <see cref="FossilDeltaSerializer" />)
        /// </summary>
        public string SerializerId;

        /// <summary>
        ///     The room's session ID
        /// </summary>
        public string SessionId;

        /// <summary>
        ///     Reconnection Token for this room session. (must be provided for client.Reconnect())
        /// </summary>
        public ReconnectionToken ReconnectionToken;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ColyseusRoom{T}" /> class.
        ///     It synchronizes state automatically with the server and send and receive messaes.
        /// </summary>
        /// <param name="name">The Room identifier</param>
        public ColyseusRoom(string name)
        {
            Name = name;
        }

        /// <summary>
        ///     Getter for the <see cref="ColyseusRoom{T}" />'s current state
        /// </summary>
        public T State
        {
            get { return Serializer.GetState(); }
        }

        [Obsolete(".Id is deprecated. Please use .RoomId instead.")]
        public string Id
        {
            get { return RoomId; }
        }

        /// <summary>
        ///     Occurs when <see cref="ColyseusClient" /> leaves this room.
        /// </summary>
        public event ColyseusCloseEventHandler OnLeave;

        /// <summary>
        ///     Implementation of <see cref="IColyseusRoom.Connect" />
        /// </summary>
        /// <returns>Response from <see cref="Connection"></see>.Connect()</returns>
        public async Awaitable Connect()
        {
            Debug.Log($"websocket {SessionId} connect async");
            await Connection.Connect();
        }

        /// <summary>
        ///     Leave the room
        /// </summary>
        /// <param name="consented">If the user agreed to this disconnection</param>
        /// <returns>Connection closure depending on user consent</returns>
        public async Awaitable Leave(bool consented = true)
        {
            if (!Connection.IsOpen) {
                return;
            }

            if (RoomId != null)
            {
                if (consented)
                {
                    await Connection.Send(new[] {ColyseusProtocol.LEAVE_ROOM});
                }
                else
                {
                    await Connection.Close();
                }
            }
            else
            {
                OnLeave?.Invoke((int)WebSocketCloseCode.Normal);
            }
        }

        // Internal OnJoin event. It is used by ColyseusClient.cs during matchmaking.
        internal event ColyseusOpenEventHandler OnJoin;

        /// <summary>
        ///     Occurs when some error has been triggered in the room.
        /// </summary>
        public event ColyseusErrorEventHandler OnError;

        /// <summary>
        ///     Occurs after applying the patched state on this <see cref="ColyseusRoom{T}" />.
        /// </summary>
        public event RoomOnStateChangeEventHandler OnStateChange;

        /// <summary>
        ///     Called by the <see cref="ColyseusClient" /> upon connection to a room
        /// </summary>
        /// <param name="colyseusConnection">The connection created by the client</param>
        public void SetConnection(ColyseusConnection connection, ColyseusRoom<T> room = null, Action devModeCloseCallback = null)
        {
            room ??= this;
            room.Connection = connection;

            connection.OnOpen += () => Debug.Log($"websocket {SessionId} opened");
            connection.OnClose += code =>
            {
                if (devModeCloseCallback == null || code == 1006)
                {
                    Debug.Log($"websocket {SessionId} closed: {code}");
                    room.OnLeave?.Invoke(code);
                }
                else
                {
                    devModeCloseCallback();
                }
            };

            // TODO: expose WebSocket error code!
            // Connection.OnError += (code, message) => OnError?.Invoke(code, message);

            connection.OnError += message => room.OnError?.Invoke(0, message);
            connection.OnMessage += room.ParseMessage;
#if USE_MESSAGEPACK_CSHARP
            connection.OnParseMessageThreaded += room.ParseMessageThreaded;
            connection.OnMessageString += room.OnMessageString;
            connection.OnMessageByte += room.OnMessageByte;
#endif
        }

        /// <summary>
        ///     Response to state changes received as messages
        /// </summary>
        /// ///
        /// <remarks>Invokes everything subscribed to <see cref="OnStateChange" /></remarks>
        /// <param name="encodedState">Byte array of the new state data</param>
        /// <param name="offset">Offset to provide the room's <see cref="Serializer" /></param>
        public void SetState(ref SequenceReader<byte> reader)
        {
            Serializer.SetState(ref reader);
            OnStateChange?.Invoke(Serializer.GetState(), true);
        }

        /// <summary>
        ///     Send a message by number type, without payload
        /// </summary>
        /// <param name="type">Message type</param>
        public async Awaitable Send(byte type)
        {
            var rent = ArrayPool<byte>.Shared.Rent(3);
            try
            {
                if (type < 0x80)
                {
                    rent[0] = ColyseusProtocol.ROOM_DATA;
                    rent[1] = type;
                    await Connection.Send(new Memory<byte>(rent, 0, 2));
                }
                else
                {
                    rent[0] = ColyseusProtocol.ROOM_DATA;
                    rent[1] = 0xcc;
                    rent[2] = type;
                    await Connection.Send(new Memory<byte>(rent, 0, 3));
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rent);
            }
        }

        /// <summary>
        ///     Send a message by number type with payload
        /// </summary>
        /// <param name="type">Message type</param>
        /// <param name="message">Message payload</param>
        public async Awaitable Send<MessageType>(byte type, MessageType message)
        {
#if USE_MESSAGEPACK_CSHARP
            var rental = SequencePool.Shared.Rent();
            var sequence = rental.Value;

            var initial = sequence.GetMemory(3);
            initial.Span[0] = ColyseusProtocol.ROOM_DATA;
            if (type < 0x80)
            {
                initial.Span[1] = type;
                sequence.Advance(2);
            }
            else
            {
                initial.Span[1] = 0xcc;
                initial.Span[2] = type;
                sequence.Advance(3);
            }

            MessagePackSerializer.Serialize(sequence, message);

            await Connection.Send(rental);
#else
            MemoryStream serializationOutput = new MemoryStream();
            MsgPack.Serialize(message, serializationOutput, SerializationOptions.SuppressTypeInformation);

            byte[] initialBytes = {ColyseusProtocol.ROOM_DATA, type};
            byte[] encodedMessage = serializationOutput.ToArray();

            byte[] bytes = new byte[initialBytes.Length + encodedMessage.Length];
            Buffer.BlockCopy(initialBytes, 0, bytes, 0, initialBytes.Length);
            Buffer.BlockCopy(encodedMessage, 0, bytes, initialBytes.Length, encodedMessage.Length);

            await Connection.Send(bytes);
#endif
        }

        /// <summary>
        ///     Send a message by string type, without payload
        /// </summary>
        /// <param name="type">Message type</param>
        public async Awaitable Send(string type)
        {
#if USE_MESSAGEPACK_CSHARP
            var rent = ArrayPool<byte>.Shared.Rent(type.Length * 2 + 6);
            var memory = new Memory<byte>(rent);

            try
            {
                var length = Encode.setInitialBytes(ColyseusProtocol.ROOM_DATA, type, memory);

                await Connection.Send(memory[..length]);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rent);
            }
#else
            byte[] encodedType = Encoding.UTF8.GetBytes(type);
            byte[] initialBytes = Encode.getInitialBytesFromEncodedType(encodedType, ColyseusProtocol.ROOM_DATA);

            byte[] bytes = new byte[initialBytes.Length + encodedType.Length];
            Buffer.BlockCopy(initialBytes, 0, bytes, 0, initialBytes.Length);
            Buffer.BlockCopy(encodedType, 0, bytes, initialBytes.Length, encodedType.Length);

            await Connection.Send(bytes);
#endif
        }

        /// <summary>
        ///     Send a message by string type with payload
        /// </summary>
        /// <param name="type">Message type</param>
        /// <param name="message">Message payload</param>
        public async Awaitable Send<MessageType>(string type, MessageType message)
        {
#if USE_MESSAGEPACK_CSHARP
            var rental = SequencePool.Shared.Rent();
            var sequence = rental.Value;

            var memory = sequence.GetMemory(type.Length * 2 + 6);
            var initialLen = Encode.setInitialBytes(ColyseusProtocol.ROOM_DATA, type, memory);
            sequence.Advance(initialLen);

            MessagePackSerializer.Serialize(sequence, message);

            await Connection.Send(rental);
#else
            MemoryStream serializationOutput = new MemoryStream();
            MsgPack.Serialize(message, serializationOutput, SerializationOptions.SuppressTypeInformation);

            byte[] encodedType = Encoding.UTF8.GetBytes(type);
            byte[] initialBytes = Encode.getInitialBytesFromEncodedType(encodedType, ColyseusProtocol.ROOM_DATA);
            byte[] encodedMessage = serializationOutput.ToArray();

            byte[] bytes = new byte[encodedType.Length + encodedMessage.Length + initialBytes.Length];
            Buffer.BlockCopy(initialBytes, 0, bytes, 0, initialBytes.Length);
            Buffer.BlockCopy(encodedType, 0, bytes, initialBytes.Length, encodedType.Length);
            Buffer.BlockCopy(encodedMessage, 0, bytes, initialBytes.Length + encodedType.Length, encodedMessage.Length);

            await Connection.Send(bytes);
#endif
        }

        /// <summary>
        ///     Send a message by number type with raw bytes payload
        /// </summary>
        /// <param name="type">Message type</param>
        /// <param name="bytes">Message payload</param>
        public async Awaitable SendBytes(byte type, byte[] bytes)
        {
#if USE_MESSAGEPACK_CSHARP
            var rent = ArrayPool<byte>.Shared.Rent(bytes.Length + 3);

            try
            {
                var rentalMemory = new Memory<byte>(rent);
                var length = 0;

                rentalMemory.Span[0] = ColyseusProtocol.ROOM_DATA_BYTES;
                if (type < 0x80)
                {
                    rentalMemory.Span[1] = type;
                    length = 2;
                }
                else
                {
                    rentalMemory.Span[1] = 0xcc;
                    rentalMemory.Span[2] = type;
                    length = 3;
                }

                bytes.CopyTo(rentalMemory[length..]);
                length += bytes.Length;

                await Connection.Send(rentalMemory[..length]);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rent);
            }
#else
            byte[] initialBytes = { ColyseusProtocol.ROOM_DATA_BYTES, type };

            byte[] bytesToSend = new byte[initialBytes.Length + bytes.Length];
            Buffer.BlockCopy(initialBytes, 0, bytesToSend, 0, initialBytes.Length);
            Buffer.BlockCopy(bytes, 0, bytesToSend, initialBytes.Length, bytes.Length);

            await Connection.Send(bytesToSend);
#endif
        }

        /// <summary>
        ///     Send a message by string type with raw bytes payload
        /// </summary>
        /// <param name="type">Message type</param>
        /// <param name="bytes">Message payload</param>
        public async Awaitable SendBytes(string type, byte[] bytes)
        {
#if USE_MESSAGEPACK_CSHARP
            var rent = ArrayPool<byte>.Shared.Rent(bytes.Length + type.Length * 2 + 6);
            var memory = new Memory<byte>(rent);

            try
            {
                var length = Encode.setInitialBytes(ColyseusProtocol.ROOM_DATA, type, memory);

                bytes.CopyTo(memory[length..]);
                length += bytes.Length;

                await Connection.Send(memory[..length]);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rent);
            }
#else
            byte[] encodedType = Encoding.UTF8.GetBytes(type);
            byte[] initialBytes = Encode.getInitialBytesFromEncodedType(encodedType, ColyseusProtocol.ROOM_DATA_BYTES);

            byte[] bytesToSend = new byte[encodedType.Length + bytes.Length + initialBytes.Length];
            Buffer.BlockCopy(initialBytes, 0, bytesToSend, 0, initialBytes.Length);
            Buffer.BlockCopy(encodedType, 0, bytesToSend, initialBytes.Length, encodedType.Length);
            Buffer.BlockCopy(bytes, 0, bytesToSend, initialBytes.Length + encodedType.Length, bytes.Length);

            await Connection.Send(bytesToSend);
#endif
        }

        /// <summary>
        ///     Method to add new message handlers to the room
        /// </summary>
        /// <param name="type">The type of message received</param>
        /// <param name="handler"></param>
        /// <typeparam name="MessageType">The type of object this message should respond with</typeparam>
        public void OnMessage<MessageType>(string type, Action<MessageType> handler)
        {
            var messageHandler = new ColyseusMessageHandler<MessageType>
            {
                Action = handler
            };

            OnMessageHandlers.TryAdd(type, messageHandler);

            OnMessageStringHandlers.TryAdd(new HandlerKey(type), new HandlerValue(type, messageHandler));
        }

        /// <summary>
        ///     Method to add new message handlers to the room
        /// </summary>
        /// <param name="type">The type of message received</param>
        /// <param name="handler"></param>
        /// <typeparam name="MessageType">The type of object this message should respond with</typeparam>
        public void OnMessage<MessageType>(byte type, Action<MessageType> handler)
        {
            OnMessageByteHandlers.TryAdd(type, new ColyseusMessageHandler<MessageType>
            {
                Action = handler
            });
        }

        public void OnMessageThreaded<MessageType>(byte type, Action<MessageType> handler)
        {
            OnMessageByteHandlers.TryAdd(type, new ColyseusMessageHandler<MessageType>
            {
                Action = handler,
                InvokeThreaded = true,
            });
        }

        /// <summary>
        ///     The function that will be called when the <see cref="Connection" /> receives a message
        /// </summary>
        /// <param name="bytes">The message as provided from the <see cref="Connection" /></param>
#if USE_MESSAGEPACK_CSHARP
        async void OnHandShakeException(string message)
        {
            await Leave(false);
            OnError?.Invoke(ColyseusErrorCode.SCHEMA_MISMATCH, message);
        }

        protected void ParseMessage(ReadOnlySequence<byte> sequence)
        {
            var reader = new SequenceReader<byte>(sequence);

            byte code = Decode.DecodeUint8(ref reader);

            if (code == ColyseusProtocol.JOIN_ROOM)
            {
                byte tokenLen = Decode.DecodeUint8(ref reader);
                string reconnectionToken = Decode.DecodeString(ref reader, tokenLen);

                tokenLen = Decode.DecodeUint8(ref reader);
                SerializerId = Decode.DecodeString(ref reader, tokenLen);

                if (SerializerId == "schema")
                {
                    try
                    {
                        Serializer = new ColyseusSchemaSerializer<T>();
                    }
                    catch (Exception e)
                    {
                        DisplaySerializerErrorHelp(e,
                            "Consider using the \"schema-codegen\" and providing the same room state for matchmaking instead of \"" +
                            typeof(T).Name + "\"");
                    }
                }
                else if (SerializerId == "fossil-delta")
                {
                    Debug.LogError(
                        "FossilDelta Serialization has been deprecated. It is highly recommended that you update your code to use the Schema Serializer. Otherwise, you must use an earlier version of the Colyseus plugin");
                }
                else
                {
                    Serializer = (IColyseusSerializer<T>)new NoneSerializer();
                }

                if (reader.Remaining > 0)
                {
                    try
                    {
                        Serializer.Handshake(ref reader);
                    }
                    catch (Exception e)
                    {
                        OnHandShakeException(e.Message);
                        return;
                    }
                }

                ReconnectionToken = new ReconnectionToken()
                {
                    RoomId = RoomId,
                    Token = reconnectionToken
                };

                OnJoin?.Invoke();

                // Acknowledge JOIN_ROOM
                _ = Connection.Send(new[] { ColyseusProtocol.JOIN_ROOM });
            }
            else if (code == ColyseusProtocol.ERROR)
            {
                float errorCode = Decode.DecodeNumber(ref reader);
                string errorMessage = Decode.DecodeString(ref reader);
                OnError?.Invoke((int)errorCode, errorMessage);
            }
            else if (code == ColyseusProtocol.LEAVE_ROOM)
            {
                _ = Leave();
            }
            else if (code == ColyseusProtocol.ROOM_STATE)
            {
                SetState(ref reader);
            }
            else if (code == ColyseusProtocol.ROOM_STATE_PATCH)
            {
                Patch(ref reader);
            }
            else if (code == ColyseusProtocol.ROOM_DATA || code == ColyseusProtocol.ROOM_DATA_BYTES)
            {
                IColyseusMessageHandler handler = null;
                object type;

                if (Decode.NumberCheck(ref reader))
                {
                    var number = Decode.DecodeNumber(ref reader);
                    type = number;
                    OnMessageByteHandlers.TryGetValue((byte)number, out handler);
                }
                else
                {
                    type = Decode.DecodeString(ref reader);
                    OnMessageHandlers.TryGetValue(type.ToString(), out handler);
                }

                if (handler != null)
                {
                    object message = null;

                    if (code == ColyseusProtocol.ROOM_DATA)
                    {
                        if (reader.Remaining > 0)
                        {
                            message = handler.Parse(reader.Sequence.Slice(reader.Consumed));
                        }
                    }
                    else if (code == ColyseusProtocol.ROOM_DATA_BYTES)
                    {
                        var remaining = (int)reader.Remaining;
                        var bytes = new byte[remaining];
                        Decode.ReadBytes(ref reader, bytes);

                        message = bytes;
                    }

                    handler.Invoke(message);
                }
                else
                {
                    Debug.LogWarning("room.OnMessage not registered for: '" + type + "'");
                }
            }
        }

        protected int ParseMessageThreaded(ReadOnlySequence<byte> sequence, out string str, out byte b, out object obj)
        {
            var reader = new SequenceReader<byte>(sequence);

            byte code = Decode.DecodeUint8(ref reader);

            if (code == ColyseusProtocol.ROOM_DATA)
            {
				object type;

				IColyseusMessageHandler handler;
				if (Decode.NumberCheck(ref reader))
				{
					var number = Decode.DecodeNumber(ref reader);
                    type = number;
                    str = default;
                    b = (byte)number;
                    OnMessageByteHandlers.TryGetValue(b, out handler);
                }
                else
                {
                    Decode.PassEncodedString(ref reader);
                    var key = new HandlerKey(sequence.Slice(0, reader.Consumed));
                    if (OnMessageStringHandlers.TryGetValue(key, out var value))
                    {
                        type = value.Type;
                        str = value.Type;
                        b = default;
                        handler = value.Handler;
                    }
                    else
                    {
                        // reset reader
                        reader = new SequenceReader<byte>(sequence);
                        reader.Advance(1);
                       
                        type = Decode.DecodeString(ref reader);
                        str = type.ToString();
                        b = default;
                        OnMessageHandlers.TryGetValue(str, out handler);                        
                    }
                }

                if (handler != null)
                {
                    if (reader.Remaining > 0)
                    {
                        try
                        {
                            obj = handler.Parse(reader.Sequence.Slice(reader.Consumed));
                        }
                        catch (Exception e)
                        {
                            Debug.LogError(e);

                            str = default;
                            b = default;
                            obj = default;
                            return -1;
                        }
                    }
                    else
                    {
                        obj = default;
                    }

                    if (handler.InvokeThreaded)
                    {
                        handler.Invoke(obj);
                        return -1;
                    }
                    else
                    {
                        return 1;
                    }
                }
                else
                {
                    Debug.LogWarning($"room.OnMessage not registered for: '{type}'");
                    str = default;
                    b = default;
                    obj = default;
                    return -1;
                }
            }

            str = default;
            b = default;
            obj = default;
            return 0;
        }

        protected void OnMessageString(string str, object obj)
        {
            if (OnMessageHandlers.TryGetValue(str, out var handler))
            {
                handler.Invoke(obj);
            }
        }

        protected void OnMessageByte(byte b, object obj)
        {
            if (OnMessageByteHandlers.TryGetValue(b, out var handler))
            {
                handler.Invoke(obj);
            }
        }
#else
        protected async void ParseMessage(byte[] bytes)
        {
            byte code = bytes[0];

            if (code == ColyseusProtocol.JOIN_ROOM)
            {
                int offset = 1;

                var reconnectionToken = Encoding.UTF8.GetString(bytes, offset + 1, bytes[offset]);
                offset += reconnectionToken.Length + 1;

                SerializerId = Encoding.UTF8.GetString(bytes, offset + 1, bytes[offset]);
                offset += SerializerId.Length + 1;

                if (SerializerId == "schema")
                {
                    try
                    {
                        Serializer = new ColyseusSchemaSerializer<T>();
                    }
                    catch (Exception e)
                    {
                        DisplaySerializerErrorHelp(e,
                            "Consider using the \"schema-codegen\" and providing the same room state for matchmaking instead of \"" +
                            typeof(T).Name + "\"");
                    }
                }
                else if (SerializerId == "fossil-delta")
                {
                    Debug.LogError(
                        "FossilDelta Serialization has been deprecated. It is highly recommended that you update your code to use the Schema Serializer. Otherwise, you must use an earlier version of the Colyseus plugin");
                }
                else
                {
                    Serializer = (IColyseusSerializer<T>) new NoneSerializer();
                }

                if (bytes.Length > offset)
                {
	                try {
		                Serializer.Handshake(bytes, offset);
	                }
	                catch (Exception e)
	                {
		                await Leave(false);
		                OnError?.Invoke(ColyseusErrorCode.SCHEMA_MISMATCH, e.Message);
		                return;
	                }
                }

                ReconnectionToken = new ReconnectionToken()
                {
                    RoomId = RoomId,
                    Token = reconnectionToken
                };

                OnJoin?.Invoke();

                // Acknowledge JOIN_ROOM
                await Connection.Send(new[] {ColyseusProtocol.JOIN_ROOM});
            }
            else if (code == ColyseusProtocol.ERROR)
            {
                Iterator it = new Iterator {Offset = 1};
                float errorCode = Decode.DecodeNumber(bytes, it);
                string errorMessage = Decode.DecodeString(bytes, it);
                OnError?.Invoke((int) errorCode, errorMessage);
            }
            else if (code == ColyseusProtocol.LEAVE_ROOM)
            {
                await Leave();
            }
            else if (code == ColyseusProtocol.ROOM_STATE)
            {
	            SetState(bytes, 1);
            }
            else if (code == ColyseusProtocol.ROOM_STATE_PATCH)
            {
                Patch(bytes, 1);
            }
            else if (code == ColyseusProtocol.ROOM_DATA || code == ColyseusProtocol.ROOM_DATA_BYTES)
            {
                IColyseusMessageHandler handler = null;
                object type;

                Iterator it = new Iterator {Offset = 1};

                if (Decode.NumberCheck(bytes, it))
                {
                    type = Decode.DecodeNumber(bytes, it);
                    OnMessageByteHandlers.TryGetValue((byte)type, out handler);
                }
                else
                {
                    type = Decode.DecodeString(bytes, it);
                    OnMessageHandlers.TryGetValue(type.ToString(), out handler);
                }

                if (handler != null)
                {
                    object message = null;

                    if ( code == ColyseusProtocol.ROOM_DATA )
                    {
                        //
                        // MsgPack deserialization can be optimized:
                        // https://github.com/deniszykov/msgpack-unity3d/issues/23
                        //
                        message = bytes.Length > it.Offset
                            ? MsgPack.Deserialize(handler.Type,
                                new MemoryStream(bytes, it.Offset, bytes.Length - it.Offset, false))
                            : null;
                    }
                    else if ( code == ColyseusProtocol.ROOM_DATA_BYTES )
                    {
                        message = new byte[bytes.Length - it.Offset];
                        Buffer.BlockCopy(bytes, it.Offset, (byte[])message, 0, bytes.Length - it.Offset);
                    }

                    handler.Invoke(message);
                }
                else
                {
                    Debug.LogWarning("room.OnMessage not registered for: '" + type + "'");
                }
            }
        }
#endif

        /// <summary>
        ///     Update the state with just the new changes to the state
        /// </summary>
        /// <remarks>Invokes everything subscribed to <see cref="OnStateChange" /></remarks>
        /// <param name="delta">The updates to the state</param>
        /// <param name="offset">Offset to provide the room's <see cref="Serializer" /></param>
        protected void Patch(ref SequenceReader<byte> reader)
        {
            Serializer.Patch(ref reader);
            OnStateChange?.Invoke(Serializer.GetState(), false);
        }

        /// <summary>
        ///     Helper function to display errors with de-serializing messages from server
        /// </summary>
        /// <param name="e">Exception information</param>
        /// <param name="helpMessage">Additional information to display</param>
        /// <exception cref="Exception">Throws <paramref name="e" /></exception>
        protected void DisplaySerializerErrorHelp(Exception e, string helpMessage)
        {
            Debug.LogWarning("The serializer from the server is: '" + SerializerId + "'. " + helpMessage);
            throw e;
        }
    }

    public static class ReadOnlySequenceExtensions
    {
        /// <summary>
        /// Compares the contents of two <see cref="ReadOnlySequence{T}"/> instances for equality.
        /// </summary>
        /// <typeparam name="T">The type of element stored in the sequences.</typeparam>
        /// <param name="left">The first sequence.</param>
        /// <param name="right">The second sequence.</param>
        /// <returns><see langword="true" /> if the sequences have equal content; <see langword="false" /> otherwise.</returns>
        /// <remarks>
        /// The underlying buffers need not be reference equal, nor must the segments in the sequences be of the same size.
        /// </remarks>
        public static bool SequenceEqual<T>(this in ReadOnlySequence<T> left, in ReadOnlySequence<T> right)
    #if !NET8_0_OR_GREATER
            where T : IEquatable<T>
    #endif
        {
            if (left.Length != right.Length)
            {
                return false;
            }

            if (left.IsSingleSegment && right.IsSingleSegment)
            {
    #if NETSTANDARD2_1 || NET
                return left.FirstSpan.SequenceEqual(right.FirstSpan);
    #else
                return left.First.Span.SequenceEqual(right.First.Span);
    #endif
            }

            ReadOnlySequence<T>.Enumerator aEnumerator = left.GetEnumerator();
            ReadOnlySequence<T>.Enumerator bEnumerator = right.GetEnumerator();

            ReadOnlySpan<T> aCurrent = default;
            ReadOnlySpan<T> bCurrent = default;
            while (true)
            {
                bool aNext = TryGetNonEmptySpan(ref aEnumerator, ref aCurrent);
                bool bNext = TryGetNonEmptySpan(ref bEnumerator, ref bCurrent);
                if (!aNext && !bNext)
                {
                    // We've reached the end of both sequences at the same time.
                    return true;
                }
                else if (aNext != bNext)
                {
                    // One ran out of bytes before the other.
                    // We don't anticipate this, because we already checked the lengths.
                    // throw Assumes.NotReachable();
                    return false;
                }

                int commonLength = Math.Min(aCurrent.Length, bCurrent.Length);
                if (!aCurrent[..commonLength].SequenceEqual(bCurrent[..commonLength]))
                {
                    return false;
                }

                aCurrent = aCurrent.Slice(commonLength);
                bCurrent = bCurrent.Slice(commonLength);
            }

            static bool TryGetNonEmptySpan(ref ReadOnlySequence<T>.Enumerator enumerator, ref ReadOnlySpan<T> span)
            {
                while (span.Length == 0)
                {
                    if (!enumerator.MoveNext())
                    {
                        return false;
                    }

                    span = enumerator.Current.Span;
                }

                return true;
            }
        }
    }
}
