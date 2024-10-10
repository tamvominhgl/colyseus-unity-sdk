namespace Colyseus
{
    /// <summary>
    ///     An empty implementation of <see cref="IColyseusSerializer{T}" />
    /// </summary>
    public class ColyseusNoneSerializer<T> : IColyseusSerializer<T>
    {
        /// <inheritdoc />
        public void SetState(byte[] rawEncodedState, int offset)
        {
        }

        /// <inheritdoc />
        public T GetState()
        {
            return default;
        }

        /// <inheritdoc />
        public void Patch(byte[] bytes, int offset)
        {
        }

        /// <inheritdoc />
        public void Teardown()
        {
        }

        /// <inheritdoc />
        public void Handshake(byte[] bytes, int offset)
        {
        }
    }
}