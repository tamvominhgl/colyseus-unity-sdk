using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using AOT;
using System.Runtime.InteropServices;
using UnityEngine;
using System.Collections;
using System.Collections.Concurrent;
using Colyseus;
using System.Buffers;

[DefaultExecutionOrder(-1)]
public class MainThreadUtil : MonoBehaviour
{
    public static event Action OnUpdate = delegate { };

#if !UNITY_2023_1_OR_NEWER
    private static MainThreadUtil Instance { get; set; }
    public static SynchronizationContext synchronizationContext { get; private set; }
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void Setup()
    {
        var instance = new GameObject("MainThreadUtil")
            .AddComponent<MainThreadUtil>();

#if !UNITY_2023_1_OR_NEWER
        Instance = instance;
        synchronizationContext = SynchronizationContext.Current;
#endif
    }

#if !UNITY_2023_1_OR_NEWER
    public static void Run(IEnumerator waitForUpdate)
    {
        synchronizationContext.Post(_ => Instance.StartCoroutine(
                    waitForUpdate), null);
    }
#endif

    void Awake()
    {
        gameObject.hideFlags = HideFlags.HideAndDontSave;
        DontDestroyOnLoad(gameObject);
    }

    void Update()
    {
        OnUpdate();
    }
}

#if !UNITY_2023_1_OR_NEWER
public class WaitForUpdate : CustomYieldInstruction
{
    public override bool keepWaiting
    {
        get { return false; }
    }

    public MainThreadAwaiter GetAwaiter()
    {
        var awaiter = new MainThreadAwaiter();
        MainThreadUtil.Run(CoroutineWrapper(this, awaiter));
        return awaiter;
    }

    public class MainThreadAwaiter : INotifyCompletion
    {
        Action continuation;

        public bool IsCompleted { get; set; }

        public void GetResult() { }

        public void Complete()
        {
            IsCompleted = true;
            continuation?.Invoke();
        }

        void INotifyCompletion.OnCompleted(Action continuation)
        {
            this.continuation = continuation;
        }
    }

    public static IEnumerator CoroutineWrapper(IEnumerator theWorker, MainThreadAwaiter awaiter)
    {
        yield return theWorker;
        awaiter.Complete();
    }
}
#endif

namespace NativeWebSocket
{
    public delegate void WebSocketOpenEventHandler();
    public delegate void WebSocketMessageEventHandler(ReadOnlySequence<byte> bytes);
    public delegate void WebSocketErrorEventHandler(string errorMsg);
    public delegate void WebSocketCloseEventHandler(int closeCode);

    public delegate bool ParseMessageHandler(ReadOnlySequence<byte> bytes, out string str, out byte b, out object message);
    public delegate void MessageStringHandler(string str, object message);
    public delegate void MessageByteHandler(byte b, object message);

    public enum WebSocketCloseCode
    {
        /* Do NOT use NotSet - it's only purpose is to indicate that the close code cannot be parsed. */
        NotSet = 0,
        Normal = 1000,
        Away = 1001,
        ProtocolError = 1002,
        UnsupportedData = 1003,
        Undefined = 1004,
        NoStatus = 1005,
        Abnormal = 1006,
        InvalidData = 1007,
        PolicyViolation = 1008,
        TooBig = 1009,
        MandatoryExtension = 1010,
        ServerError = 1011,
        TlsHandshakeFailure = 1015
    }

    public enum WebSocketState
    {
        Connecting,
        Open,
        Closing,
        Closed
    }

    public interface IWebSocket
    {
        event WebSocketOpenEventHandler OnOpen;
        event WebSocketMessageEventHandler OnMessage;
        event WebSocketErrorEventHandler OnError;
        event WebSocketCloseEventHandler OnClose;

        WebSocketState State { get; }
    }

    public static class WebSocketHelpers
    {
        public static WebSocketCloseCode ParseCloseCodeEnum(int closeCode)
        {

            if (Enum.IsDefined(typeof(WebSocketCloseCode), closeCode))
            {
                return (WebSocketCloseCode)closeCode;
            }
            else
            {
                return WebSocketCloseCode.Undefined;
            }

        }

        public static WebSocketException GetErrorMessageFromCode(int errorCode, Exception inner)
        {
            switch (errorCode)
            {
                case -1:
                    return new WebSocketUnexpectedException("WebSocket instance not found.", inner);
                case -2:
                    return new WebSocketInvalidStateException("WebSocket is already connected or in connecting state.", inner);
                case -3:
                    return new WebSocketInvalidStateException("WebSocket is not connected.", inner);
                case -4:
                    return new WebSocketInvalidStateException("WebSocket is already closing.", inner);
                case -5:
                    return new WebSocketInvalidStateException("WebSocket is already closed.", inner);
                case -6:
                    return new WebSocketInvalidStateException("WebSocket is not in open state.", inner);
                case -7:
                    return new WebSocketInvalidArgumentException("Cannot close WebSocket. An invalid code was specified or reason is too long.", inner);
                default:
                    return new WebSocketUnexpectedException("Unknown error.", inner);
            }
        }
    }

    public class WebSocketException : Exception
    {
        public WebSocketException() { }
        public WebSocketException(string message) : base(message) { }
        public WebSocketException(string message, Exception inner) : base(message, inner) { }
    }

    public class WebSocketUnexpectedException : WebSocketException
    {
        public WebSocketUnexpectedException() { }
        public WebSocketUnexpectedException(string message) : base(message) { }
        public WebSocketUnexpectedException(string message, Exception inner) : base(message, inner) { }
    }

    public class WebSocketInvalidArgumentException : WebSocketException
    {
        public WebSocketInvalidArgumentException() { }
        public WebSocketInvalidArgumentException(string message) : base(message) { }
        public WebSocketInvalidArgumentException(string message, Exception inner) : base(message, inner) { }
    }

    public class WebSocketInvalidStateException : WebSocketException
    {
        public WebSocketInvalidStateException() { }
        public WebSocketInvalidStateException(string message) : base(message) { }
        public WebSocketInvalidStateException(string message, Exception inner) : base(message, inner) { }
    }

#if !UNITY_2023_1_OR_NEWER
    public class WaitForBackgroundThread
    {
        public ConfiguredTaskAwaitable.ConfiguredTaskAwaiter GetAwaiter()
        {
            return Task.Run(() => { }).ConfigureAwait(false).GetAwaiter();
        }
    }
#endif

#if UNITY_WEBGL && !UNITY_EDITOR

  /// <summary>
  /// WebSocket class bound to JSLIB.
  /// </summary>
  public class WebSocket : IWebSocket {

    /* WebSocket JSLIB functions */
    [DllImport ("__Internal")]
    public static extern int WebSocketConnect (int instanceId);

    [DllImport ("__Internal")]
    public static extern int WebSocketClose (int instanceId, int code, string reason);

    [DllImport ("__Internal")]
    public static extern int WebSocketSend (int instanceId, byte[] dataPtr, int dataLength);

    [DllImport ("__Internal")]
    public static extern int WebSocketSendText (int instanceId, string message);

    [DllImport ("__Internal")]
    public static extern int WebSocketGetState (int instanceId);

    protected int instanceId;

    public event WebSocketOpenEventHandler OnOpen;
    public event WebSocketMessageEventHandler OnMessage;
    public event WebSocketErrorEventHandler OnError;
    public event WebSocketCloseEventHandler OnClose;

    public WebSocket (string url, Dictionary<string, string> headers = null) {
      if (!WebSocketFactory.isInitialized) {
        WebSocketFactory.Initialize ();
      }

      int instanceId = WebSocketFactory.WebSocketAllocate (url);
      WebSocketFactory.instances.Add (instanceId, this);

      this.instanceId = instanceId;
    }

    ~WebSocket () {
      WebSocketFactory.HandleInstanceDestroy (this.instanceId);
    }

    public int GetInstanceId () {
      return this.instanceId;
    }

    public Task Connect () {
      int ret = WebSocketConnect (this.instanceId);

      if (ret < 0)
        throw WebSocketHelpers.GetErrorMessageFromCode (ret, null);

      return Task.CompletedTask;
    }

    public void CancelConnection () {
        if (State == WebSocketState.Open)
            Close (WebSocketCloseCode.Abnormal);
    }

    public Task Close (WebSocketCloseCode code = WebSocketCloseCode.Normal, string reason = null) {
      int ret = WebSocketClose (this.instanceId, (int) code, reason);

      if (ret < 0)
        throw WebSocketHelpers.GetErrorMessageFromCode (ret, null);

      return Task.CompletedTask;
    }

    public Task Send (byte[] data) {
      int ret = WebSocketSend (this.instanceId, data, data.Length);

      if (ret < 0)
        throw WebSocketHelpers.GetErrorMessageFromCode (ret, null);

      return Task.CompletedTask;
    }

    public Task SendText (string message) {
      int ret = WebSocketSendText (this.instanceId, message);

      if (ret < 0)
        throw WebSocketHelpers.GetErrorMessageFromCode (ret, null);

      return Task.CompletedTask;
    }

    public WebSocketState State {
      get {
        int state = WebSocketGetState (this.instanceId);

        if (state < 0)
          throw WebSocketHelpers.GetErrorMessageFromCode (state, null);

        switch (state) {
          case 0:
            return WebSocketState.Connecting;

          case 1:
            return WebSocketState.Open;

          case 2:
            return WebSocketState.Closing;

          case 3:
            return WebSocketState.Closed;

          default:
            return WebSocketState.Closed;
        }
      }
    }

    public void DelegateOnOpenEvent () {
        this.OnOpen?.Invoke ();
    }

    public void DelegateOnMessageEvent (byte[] data) {
        this.OnMessage?.Invoke (data);
    }

    public void DelegateOnErrorEvent (string errorMsg) {
        this.OnError?.Invoke (errorMsg);
    }

    public void DelegateOnCloseEvent (int closeCode) {
        this.OnClose?.Invoke (closeCode);
    }

  }

#else

    public class WebSocket : IWebSocket
    {
        public event WebSocketOpenEventHandler OnOpen;
        public event WebSocketMessageEventHandler OnMessage;
        public event WebSocketErrorEventHandler OnError;
        public event WebSocketCloseEventHandler OnClose;

        public event ParseMessageHandler OnParseMessageThreaded;
        public event MessageStringHandler OnMessageString;
        public event MessageByteHandler OnMessageByte;

        private readonly Uri uri;
        private readonly Dictionary<string, string> headers;
        private ClientWebSocket m_Socket;

        private CancellationTokenSource m_TokenSource;
        private CancellationToken m_CancellationToken;

        private SemaphoreSlim m_Semaphore = new(1, 1);
        private readonly ConcurrentQueue<Event> m_Events = new();

        private bool dispatcherRegistered = false;

        internal enum EventType
        {
            Unknown,
            Open,
            Message,
            Error,

            MessageString,
            MessageByte,
        }

        internal readonly struct Event
        {
            public EventType Type { get; }

            public SequencePool.Rental Rental { get; }

            public string Message { get; }

            public object Object { get; }
            public byte Byte { get; }

            public Event(SequencePool.Rental rental)
            {
                Type = EventType.Message;
                Rental = rental;
                Message = default;
                Byte = default;
                Object = default;
            }

            public Event(string str, object obj)
            {
                Type = EventType.MessageString;
                Rental = default;
                Message = str;
                Byte = default;
                Object = obj;
            }

            public Event(byte b, object obj)
            {
                Type = EventType.MessageString;
                Rental = default;
                Message = default;
                Byte = default;
                Object = obj;
            }

            public Event(EventType type, string message = default)
            {
                Type = type;
                Rental = default;
                Message = message;
                Byte = default;
                Object = default;
            }
        }

        public WebSocket(string url, Dictionary<string, string> headers = null)
        {
            uri = new Uri(url);

            if (headers == null)
            {
                this.headers = new Dictionary<string, string>();
            }
            else
            {
                this.headers = headers;
            }

            string protocol = uri.Scheme;
            if (!protocol.Equals("ws") && !protocol.Equals("wss"))
                throw new ArgumentException("Unsupported protocol: " + protocol);
        }

        public void CancelConnection()
        {
            m_TokenSource?.Cancel();
        }

        public async Task Connect()
        {
            try
            {
                m_TokenSource = new CancellationTokenSource();
                m_CancellationToken = m_TokenSource.Token;

                m_Socket = new ClientWebSocket();

                foreach (var header in headers)
                {
                    m_Socket.Options.SetRequestHeader(header.Key, header.Value);
                }

                if (!dispatcherRegistered)
                {
                    dispatcherRegistered = true;
                    MainThreadUtil.OnUpdate += DispatchMessageQueue;
                }

                await m_Socket.ConnectAsync(uri, m_CancellationToken).ConfigureAwait(false);

                m_Events.Enqueue(new Event(EventType.Open));

                await Receive().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
#if UNITY_2023_1_OR_NEWER
                await Awaitable.MainThreadAsync();
#else
                await new WaitForUpdate();
#endif
                OnError?.Invoke(ex.Message);
                OnClose?.Invoke((int)WebSocketCloseCode.Abnormal);
            }
            finally
            {
                if (m_Socket != null)
                {
                    m_TokenSource.Cancel();
                    m_Socket.Dispose();
                }

                m_Semaphore?.Dispose();
                m_Semaphore = null;

                MainThreadUtil.OnUpdate -= DispatchMessageQueue;
            }
        }

        public WebSocketState State => m_Socket?.State switch
        {
            System.Net.WebSockets.WebSocketState.Connecting => WebSocketState.Connecting,
            System.Net.WebSockets.WebSocketState.Open => WebSocketState.Open,
            System.Net.WebSockets.WebSocketState.CloseSent => WebSocketState.Closing,
            System.Net.WebSockets.WebSocketState.CloseReceived => WebSocketState.Closing,
            _ => WebSocketState.Closed
        };

        // public Task Send(byte[] bytes)
        //     => SendMessage(WebSocketMessageType.Binary, bytes);

        // public Task SendText(string message)
        //     => SendMessage(WebSocketMessageType.Text, Encoding.UTF8.GetBytes(message));

        protected async Task SendMessage(WebSocketMessageType messageType, ReadOnlyMemory<byte> buffer, bool endOfMessage = true)
        {
            // Return control to the calling method immediately.
            // await Task.Yield ();

            // Make sure we have data.
            if (buffer.Length == 0)
            {
                return;
            }

            try
            {
                await m_Semaphore.WaitAsync(m_CancellationToken).ConfigureAwait(false);

                if (State != WebSocketState.Open)
                {
                    throw new InvalidOperationException("WebSocket is not ready!");
                }

                await m_Socket.SendAsync(buffer, messageType, endOfMessage, m_CancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                switch (e)
                {
                    case TaskCanceledException:
                    case OperationCanceledException:
                        break;
                    default:
                        Debug.LogException(e);
                        m_Events.Enqueue(new Event(EventType.Error, e.Message));
                        break;
                }
            }
            finally
            {
                m_Semaphore?.Release();
            }
        }

        // simple dispatcher for queued messages.
        void DispatchMessageQueue()
        {
            while (m_Events.TryDequeue(out var evt))
            {
                try
                {
                    switch (evt.Type)
                    {
                        case EventType.Open:
                            OnOpen?.Invoke();
                            break;
                        case EventType.Message:
                            {
                                using var rental = evt.Rental;
                                OnMessage?.Invoke(rental.Value);
                            }
                            break;
                        case EventType.MessageString:
                            OnMessageString?.Invoke(evt.Message, evt.Object);
                            break;
                        case EventType.MessageByte:
                            OnMessageByte?.Invoke(evt.Byte, evt.Object);
                            break;
                        case EventType.Error:
                            OnError?.Invoke(evt.Message);
                            break;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    OnError?.Invoke(e.Message);
                }
            }
        }

        async Task Receive()
        {
            int closeCode = (int)WebSocketCloseCode.Abnormal;
#if UNITY_2023_1_OR_NEWER
            await Awaitable.BackgroundThreadAsync();
#else
            await new WaitForBackgroundThread();
#endif

            SequencePool.Rental rental = default;

            try
            {
                while (m_Socket.State == System.Net.WebSockets.WebSocketState.Open)
                {
                    ValueWebSocketReceiveResult result;
                    rental = SequencePool.Shared.Rent();

                    do
                    {
                        var memory = rental.Value.GetMemory(8192);
                        result = await m_Socket.ReceiveAsync(memory, m_CancellationToken).ConfigureAwait(false);
                        rental.Value.Advance(result.Count);
                    } while (!result.EndOfMessage);

                    if (result.MessageType != WebSocketMessageType.Close)
                    {
                        if (OnParseMessageThreaded?.Invoke(rental.Value, out var str, out var b, out var obj) == true)
                        {
                            if (string.IsNullOrEmpty(str))
                            {
                                m_Events.Enqueue(new Event(b, obj));
                            }
                            else
                            {
                                m_Events.Enqueue(new Event(str, obj));
                            }
                            rental.Dispose();
                        }
                        else
                        {
                            m_Events.Enqueue(new Event(rental));
                        }
                        rental = default;
                    }
                    else
                    {
                        await Close().ConfigureAwait(false);
                        closeCode = (int)m_Socket.CloseStatus;
                        break;
                    }
                }
            }
            catch (Exception)
            {
                m_TokenSource.Cancel();
            }
            finally
            {
                rental.Dispose();

#if UNITY_2023_1_OR_NEWER
                await Awaitable.MainThreadAsync();
#else
                await new WaitForUpdate();
#endif
                // to make sure all data are dispatched before OnClose
                DispatchMessageQueue();
                OnClose?.Invoke(closeCode);
            }
        }

        public async Task Close()
        {
            if (State == WebSocketState.Open)
            {
                await m_Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, m_CancellationToken).ConfigureAwait(false);
            }
        }
    }
#endif

    ///
    /// Factory
    ///

    /// <summary>
    /// Class providing static access methods to work with JSLIB WebSocket or WebSocketSharp interface
    /// </summary>
    public static class WebSocketFactory
    {

#if UNITY_WEBGL && !UNITY_EDITOR
    /* Map of websocket instances */
    public static Dictionary<Int32, WebSocket> instances = new Dictionary<Int32, WebSocket> ();

    /* Delegates */
    public delegate void OnOpenCallback (int instanceId);
    public delegate void OnMessageCallback (int instanceId, System.IntPtr msgPtr, int msgSize);
    public delegate void OnErrorCallback (int instanceId, System.IntPtr errorPtr);
    public delegate void OnCloseCallback (int instanceId, int closeCode);

    /* WebSocket JSLIB callback setters and other functions */
    [DllImport ("__Internal")]
    public static extern int WebSocketAllocate (string url);

    [DllImport ("__Internal")]
    public static extern void WebSocketFree (int instanceId);

    [DllImport ("__Internal")]
    public static extern void WebSocketSetOnOpen (OnOpenCallback callback);

    [DllImport ("__Internal")]
    public static extern void WebSocketSetOnMessage (OnMessageCallback callback);

    [DllImport ("__Internal")]
    public static extern void WebSocketSetOnError (OnErrorCallback callback);

    [DllImport ("__Internal")]
    public static extern void WebSocketSetOnClose (OnCloseCallback callback);

    /* If callbacks was initialized and set */
    public static bool isInitialized = false;

    /*
     * Initialize WebSocket callbacks to JSLIB
     */
    public static void Initialize () {

      WebSocketSetOnOpen (DelegateOnOpenEvent);
      WebSocketSetOnMessage (DelegateOnMessageEvent);
      WebSocketSetOnError (DelegateOnErrorEvent);
      WebSocketSetOnClose (DelegateOnCloseEvent);

      isInitialized = true;

    }

    /// <summary>
    /// Called when instance is destroyed (by destructor)
    /// Method removes instance from map and free it in JSLIB implementation
    /// </summary>
    /// <param name="instanceId">Instance identifier.</param>
    public static void HandleInstanceDestroy (int instanceId) {

      instances.Remove (instanceId);
      WebSocketFree (instanceId);

    }

    [MonoPInvokeCallback (typeof (OnOpenCallback))]
    public static void DelegateOnOpenEvent (int instanceId) {

      WebSocket instanceRef;

      if (instances.TryGetValue (instanceId, out instanceRef)) {
        instanceRef.DelegateOnOpenEvent ();
      }

    }

    [MonoPInvokeCallback (typeof (OnMessageCallback))]
    public static void DelegateOnMessageEvent (int instanceId, System.IntPtr msgPtr, int msgSize) {

      WebSocket instanceRef;

      if (instances.TryGetValue (instanceId, out instanceRef)) {
        byte[] msg = new byte[msgSize];
        Marshal.Copy (msgPtr, msg, 0, msgSize);

        instanceRef.DelegateOnMessageEvent (msg);
      }

    }

    [MonoPInvokeCallback (typeof (OnErrorCallback))]
    public static void DelegateOnErrorEvent (int instanceId, System.IntPtr errorPtr) {

      WebSocket instanceRef;

      if (instances.TryGetValue (instanceId, out instanceRef)) {

        string errorMsg = Marshal.PtrToStringAuto (errorPtr);
        instanceRef.DelegateOnErrorEvent (errorMsg);

      }

    }

    [MonoPInvokeCallback (typeof (OnCloseCallback))]
    public static void DelegateOnCloseEvent (int instanceId, int closeCode) {

      WebSocket instanceRef;

      if (instances.TryGetValue (instanceId, out instanceRef)) {
        instanceRef.DelegateOnCloseEvent (closeCode);
      }

    }
#endif

        /// <summary>
        /// Create WebSocket client instance
        /// </summary>
        /// <returns>The WebSocket instance.</returns>
        /// <param name="url">WebSocket valid URL.</param>
        public static WebSocket CreateInstance(string url)
        {
            return new WebSocket(url);
        }

    }

}
