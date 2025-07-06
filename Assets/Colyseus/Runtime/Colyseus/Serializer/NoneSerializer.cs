using System.Buffers;

namespace Colyseus
{
    // TODO: remove dummy state dependency from NoneSerializer.
    public class NoState : Schema.Schema { }

    /// <summary>
    ///     An empty implementation of <see cref="IColyseusSerializer{T}" />
    /// </summary>
    public class NoneSerializer : IColyseusSerializer<NoState>
    {
        NoState state = new NoState();

        /// <inheritdoc />
        public void SetState(ref SequenceReader<byte> reader)
        {
        }

        /// <inheritdoc />
        public NoState GetState()
        {
            return state;
        }

        /// <inheritdoc />
        public void Patch(ref SequenceReader<byte> reader)
        {
        }

        /// <inheritdoc />
        public void Teardown()
        {
        }

        /// <inheritdoc />
        public void Handshake(ref SequenceReader<byte> reader)
        {
        }
    }
}