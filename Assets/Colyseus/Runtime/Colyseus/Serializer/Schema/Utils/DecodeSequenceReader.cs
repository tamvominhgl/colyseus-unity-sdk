using System;
using System.Buffers;
using System.Text;
using MiscUtil.Conversion;

namespace Colyseus.Schema.Utils
{
    public partial class Decode
    {
        /// <summary>
        ///     Decode method to decode <paramref name="bytes" /> into a <see cref="float" />
        /// </summary>
        /// <param name="bytes">The incoming data</param>
        /// <param name="it">The iterator who's <see cref="Iterator.Offset" /> will be used to Decode the data</param>
        /// <returns><paramref name="bytes" /> decoded into a <see cref="float" /></returns>
        public static float DecodeNumber(ref SequenceReader<byte> reader)
        {
            reader.TryRead(out byte prefix);

            if (prefix < 0x80)
            {
                // positive fixint
                return prefix;
            }

            if (prefix == 0xca)
            {
                // float 32
                return DecodeFloat32(ref reader);
            }

            if (prefix == 0xcb)
            {
                // float 64
                return (float)DecodeFloat64(ref reader);
            }

            if (prefix == 0xcc)
            {
                // uint 8
                return DecodeUint8(ref reader);
            }

            if (prefix == 0xcd)
            {
                // uint 16
                return DecodeUint16(ref reader);
            }

            if (prefix == 0xce)
            {
                // uint 32
                return DecodeUint32(ref reader);
            }

            if (prefix == 0xcf)
            {
                // uint 64
                return DecodeUint64(ref reader);
            }

            if (prefix == 0xd0)
            {
                // int 8
                return DecodeInt8(ref reader);
            }

            if (prefix == 0xd1)
            {
                // int 16
                return DecodeInt16(ref reader);
            }

            if (prefix == 0xd2)
            {
                // int 32
                return DecodeInt32(ref reader);
            }

            if (prefix == 0xd3)
            {
                // int 64
                return DecodeInt64(ref reader);
            }

            if (prefix > 0xdf)
            {
                // negative fixint
                return (0xff - prefix + 1) * -1;
            }

            return float.NaN;
        }

        /// <summary>
        ///     Decode method to decode <paramref name="bytes" /> into an 8-bit <see cref="int" />
        /// </summary>
        /// <param name="bytes">The incoming data</param>
        /// <param name="it">The iterator who's <see cref="Iterator.Offset" /> will be used to Decode the data</param>
        /// <returns><paramref name="bytes" /> decoded into an 8-bit <see cref="int" /></returns>
        public static sbyte DecodeInt8(ref SequenceReader<byte> reader)
        {
            return Convert.ToSByte(DecodeUint8(ref reader));
        }

        /// <summary>
        ///     Decode method to decode <paramref name="bytes" /> into an 8-bit <see cref="uint" />
        /// </summary>
        /// <param name="bytes">The incoming data</param>
        /// <param name="it">The iterator who's <see cref="Iterator.Offset" /> will be used to Decode the data</param>
        /// <returns><paramref name="bytes" /> decoded into an 8-bit <see cref="uint" /></returns>
        public static byte DecodeUint8(ref SequenceReader<byte> reader)
        {
            reader.TryRead(out byte value);
            return value;
        }

        /// <summary>
        ///     Decode method to decode <paramref name="bytes" /> into a 16-bit <see cref="int" />
        /// </summary>
        /// <param name="bytes">The incoming data</param>
        /// <param name="it">The iterator who's <see cref="Iterator.Offset" /> will be used to Decode the data</param>
        /// <returns><paramref name="bytes" /> decoded into a 16-bit <see cref="int" /></returns>
        public static short DecodeInt16(ref SequenceReader<byte> reader)
        {
            reader.TryReadLittleEndian(out short value);
            return value;
        }

        /// <summary>
        ///     Decode method to decode <paramref name="bytes" /> into a 16-bit <see cref="uint" />
        /// </summary>
        /// <param name="bytes">The incoming data</param>
        /// <param name="it">The iterator who's <see cref="Iterator.Offset" /> will be used to Decode the data</param>
        /// <returns><paramref name="bytes" /> decoded into a 16-bit <see cref="uint" /></returns>
        public static ushort DecodeUint16(ref SequenceReader<byte> reader)
        {
            return Convert.ToUInt16(DecodeInt16(ref reader));
        }

        /// <summary>
        ///     Decode method to decode <paramref name="bytes" /> into a 32-bit <see cref="int" />
        /// </summary>
        /// <param name="bytes">The incoming data</param>
        /// <param name="it">The iterator who's <see cref="Iterator.Offset" /> will be used to Decode the data</param>
        /// <returns><paramref name="bytes" /> decoded into a 32-bit <see cref="int" /></returns>
        public static int DecodeInt32(ref SequenceReader<byte> reader)
        {
            reader.TryReadLittleEndian(out int value);
            return value;
        }

        /// <summary>
        ///     Decode method to decode <paramref name="bytes" /> into a 32-bit <see cref="uint" />
        /// </summary>
        /// <param name="bytes">The incoming data</param>
        /// <param name="it">The iterator who's <see cref="Iterator.Offset" /> will be used to Decode the data</param>
        /// <returns><paramref name="bytes" /> decoded into a 32-bit <see cref="uint" /></returns>
        public static uint DecodeUint32(ref SequenceReader<byte> reader)
        {
            return Convert.ToUInt32(DecodeInt32(ref reader));
        }

        /// <summary>
        ///     Decode method to decode <paramref name="bytes" /> into a 32-bit <see cref="float" />
        /// </summary>
        /// <param name="bytes">The incoming data</param>
        /// <param name="it">The iterator who's <see cref="Iterator.Offset" /> will be used to Decode the data</param>
        /// <returns><paramref name="bytes" /> decoded into a 32-bit <see cref="float" /></returns>
        public static float DecodeFloat32(ref SequenceReader<byte> reader)
        {
            var bytes = ArrayPool<byte>.Shared.Rent(4);
            ReadBytes(ref reader, new Span<byte>(bytes, 0, 4));
            var value = bitConverter.ToSingle(bytes, 0);
            ArrayPool<byte>.Shared.Return(bytes);
            return value;
        }

        /// <summary>
        ///     Decode method to decode <paramref name="bytes" /> into a 64-bit <see cref="float" />
        /// </summary>
        /// <param name="bytes">The incoming data</param>
        /// <param name="it">The iterator who's <see cref="Iterator.Offset" /> will be used to Decode the data</param>
        /// <returns><paramref name="bytes" /> decoded into a 64-bit <see cref="float" /></returns>
        public static double DecodeFloat64(ref SequenceReader<byte> reader)
        {
            var bytes = ArrayPool<byte>.Shared.Rent(8);
            ReadBytes(ref reader, new Span<byte>(bytes, 0, 8));
            var value = bitConverter.ToDouble(bytes, 0);
            ArrayPool<byte>.Shared.Return(bytes);
            return value;
        }

        /// <summary>
        ///     Decode method to decode <paramref name="bytes" /> into a 64-bit <see cref="int" />
        /// </summary>
        /// <param name="bytes">The incoming data</param>
        /// <param name="it">The iterator who's <see cref="Iterator.Offset" /> will be used to Decode the data</param>
        /// <returns><paramref name="bytes" /> decoded into a 64-bit <see cref="int" /></returns>
        public static long DecodeInt64(ref SequenceReader<byte> reader)
        {
            reader.TryReadLittleEndian(out long value);
            return value;
        }

        /// <summary>
        ///     Decode method to decode <paramref name="bytes" /> into a 64-bit <see cref="uint" />
        /// </summary>
        /// <param name="bytes">The incoming data</param>
        /// <param name="it">The iterator who's <see cref="Iterator.Offset" /> will be used to Decode the data</param>
        /// <returns><paramref name="bytes" /> decoded into a 64-bit <see cref="uint" /></returns>
        public static ulong DecodeUint64(ref SequenceReader<byte> reader)
        {
            return Convert.ToUInt64(DecodeInt64(ref reader));
        }

        /// <summary>
        ///     Decode method to decode <paramref name="bytes" /> into a <see cref="bool" />
        /// </summary>
        /// <param name="bytes">The incoming data</param>
        /// <param name="it">The iterator who's <see cref="Iterator.Offset" /> will be used to Decode the data</param>
        /// <returns><paramref name="bytes" /> decoded into a <see cref="bool" /></returns>
        public static bool DecodeBoolean(ref SequenceReader<byte> reader)
        {
            return DecodeUint8(ref reader) > 0;
        }

        /// <summary>
        ///     Decode method to decode <paramref name="bytes" /> into a <see cref="string" />
        /// </summary>
        /// <param name="bytes">The incoming data</param>
        /// <param name="it">The iterator who's <see cref="Iterator.Offset" /> will be used to Decode the data</param>
        /// <returns><paramref name="bytes" /> decoded into a <see cref="string" /></returns>
        public static string DecodeString(ref SequenceReader<byte> reader)
        {
            reader.TryRead(out byte prefix);

            int length;
            if (prefix < 0xc0)
            {
                // fixstr
                length = prefix & 0x1f;
            }
            else if (prefix == 0xd9)
            {
                length = DecodeUint8(ref reader);
            }
            else if (prefix == 0xda)
            {
                length = DecodeUint16(ref reader);
            }
            else if (prefix == 0xdb)
            {
                length = (int)DecodeUint32(ref reader);
            }
            else
            {
                length = 0;
            }

            return DecodeString(ref reader, length);
        }

        public static string DecodeString(ref SequenceReader<byte> reader, int byteLength)
        {
            var unreadSpan = reader.UnreadSpan;
            if (unreadSpan.Length >= byteLength)
            {
                var str = Encoding.UTF8.GetString(unreadSpan[..byteLength]);
                reader.Advance(byteLength);
                return str;
            }
            else
            {
                int remainingByteLength = byteLength;
                int maxCharLength = Encoding.UTF8.GetMaxCharCount(remainingByteLength);
                char[] charArray = ArrayPool<char>.Shared.Rent(maxCharLength);
                Decoder decoder = Encoding.UTF8.GetDecoder();

                int initializedChars = 0;
                while (remainingByteLength > 0)
                {
                    int bytesRead = Math.Min(remainingByteLength, reader.UnreadSpan.Length);
                    remainingByteLength -= bytesRead;
                    bool flush = remainingByteLength == 0;

                    initializedChars += decoder.GetChars(reader.UnreadSpan[..bytesRead], charArray.AsSpan(initializedChars), flush);

                    reader.Advance(bytesRead);
                }

                var str = new string(charArray, 0, initializedChars);
                ArrayPool<char>.Shared.Return(charArray);
                return str;
            }
        }

        public static void PassEncodedString(ref SequenceReader<byte> reader)
        {
            reader.TryRead(out byte prefix);

            int length;
            if (prefix < 0xc0)
            {
                // fixstr
                length = prefix & 0x1f;
            }
            else if (prefix == 0xd9)
            {
                length = DecodeUint8(ref reader);
            }
            else if (prefix == 0xda)
            {
                length = DecodeUint16(ref reader);
            }
            else if (prefix == 0xdb)
            {
                length = (int)DecodeUint32(ref reader);
            }
            else
            {
                length = 0;
            }

            reader.Advance(length);
        }

        /// <summary>
        ///     Checks if the incoming <paramref name="bytes" /> is a number
        /// </summary>
        /// <param name="bytes">The incoming data</param>
        /// <param name="it">The iterator who's <see cref="Iterator.Offset" /> will be used to Decode the data</param>
        /// <returns>True if <paramref name="bytes" /> can be resolved into a number, false otherwise</returns>
        public static bool NumberCheck(ref SequenceReader<byte> reader)
        {
            reader.TryPeek(out byte prefix);
            return prefix < 0x80 || prefix >= 0xca && prefix <= 0xd3;
        }
        
        public static bool ReadBytes(ref SequenceReader<byte> reader, Span<byte> dest)
        {
            if (reader.TryCopyTo(dest))
            {
                reader.Advance(dest.Length);
                return true;
            }
            else
            {
                return false;
            }
        }
    }
}

