using System;
#if USE_MESSAGEPACK_CSHARP
using System.IO;
using MessagePack;
#endif

namespace Colyseus
{
    /// <summary>
    ///     Base interface for MessageHandlers
    /// </summary>
    public interface IColyseusMessageHandler
    {
        /// <summary>
        ///     Message Type
        /// </summary>
        Type Type { get; }

        /// <summary>
        ///     Base invocation for the MessageHandler
        /// </summary>
        /// <param name="message">The data to be passed into the function</param>
        void Invoke(object message);

#if USE_MESSAGEPACK_CSHARP
        void ParseAndInvoke(MemoryStream stream);
#endif
    }

    /// <summary>
    ///     Base Implementation of the IMessageHandler interface
    /// </summary>
    /// <typeparam name="T">Message Type</typeparam>
    public class ColyseusMessageHandler<T> : IColyseusMessageHandler
    {
        /// <summary>
        ///     The Action this message will invoke
        /// </summary>
        public Action<T> Action;

        /// <summary>
        ///     Invokes this message's Action
        /// </summary>
        /// <param name="message">Data for the Action, will be cast to "T"</param>
        public void Invoke(object message)
        {
            Action.Invoke((T) message);
        }

#if USE_MESSAGEPACK_CSHARP
        public void ParseAndInvoke(MemoryStream stream)
        {
            var message = MessagePackSerializer.Deserialize<T>(stream);
            Action.Invoke((T) message);
        }
#endif

        /// <summary>
        ///     Implementation of the interface Type
        /// </summary>
        /// <returns>typeof(T)</returns>
        public Type Type
        {
            get { return typeof(T); }
        }
    }
}