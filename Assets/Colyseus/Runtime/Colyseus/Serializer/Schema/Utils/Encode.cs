using System;
using System.Text;

namespace Colyseus.Schema.Utils
{
	public class Encode
	{
        /// <summary>
        ///     Retrieves the initial bytes from <paramref name="encodedType" /> based on it's length
        /// </summary>
        /// <param name="encodedType">The incoming "type" encoded to a <see cref="byte" />[]</param>
        /// <returns>The important bytes we need based upon the incoming type</returns>
        /// <exception cref="Exception"></exception>
        public static byte[] getInitialBytesFromEncodedType(byte[] encodedType, byte protocol)
        {
            byte[] initialBytes = { protocol };

            if (encodedType.Length < 0x20)
            {
                initialBytes = addByteToArray(initialBytes, new[] { (byte)(encodedType.Length | 0xa0) });
            }
            else if (encodedType.Length < 0x100)
            {
                initialBytes = addByteToArray(initialBytes, new byte[] { 0xd9 });
                initialBytes = uint8(initialBytes, encodedType.Length);
            }
            else if (encodedType.Length < 0x10000)
            {
                initialBytes = addByteToArray(initialBytes, new byte[] { 0xda });
                initialBytes = uint16(initialBytes, encodedType.Length);
            }
            else if (encodedType.Length < 0x7fffffff)
            {
                initialBytes = addByteToArray(initialBytes, new byte[] { 0xdb });
                initialBytes = uint32(initialBytes, encodedType.Length);
            }
            else
            {
                throw new Exception("String too long");
            }

            return initialBytes;
        }

        public static int setInitialBytes(byte protocol, string type, Memory<byte> memory)
        {
            var span = memory.Span;
            span[0] = protocol;

            int offset;
            var stringLen = type.Length;
            if (stringLen < 0x20)
            {
                offset = 2;
            }
            else if (stringLen < 0x100)
            {
                offset = 3;
            }
            else if (stringLen < 0x10000)
            {
                offset = 4;
            }
            else if (stringLen < 0x7fffffff)
            {
                offset = 6;
            }
            else
            {
                throw new Exception("String too long");
            }

            var byteLen = Encoding.UTF8.GetBytes(type, memory.Span[offset..]);

            if (byteLen != stringLen)
            {
                var estimateOffset = offset;

                if (byteLen < 0x20)
                {
                    offset = 2;
                }
                else if (byteLen < 0x100)
                {
                    offset = 3;
                }
                else if (byteLen < 0x10000)
                {
                    offset = 4;
                }
                else if (byteLen < 0x7fffffff)
                {
                    offset = 6;
                }
                else
                {
                    throw new Exception("String too long");
                }

                if (offset != estimateOffset)
                {
                    var src = memory.Span[estimateOffset..];
                    var dst = memory.Span[offset..];

                    for (var i = 0; i < byteLen; i++)
                    {
                        dst[i] = src[i];
                    }
                }
            }

            var length = 1;
            if (byteLen < 0x20)
            {
                span[1] = (byte)(byteLen | 0xa0);
                length += 1;
            }
            else if (byteLen < 0x100)
            {
                span[1] = 0xd9;
                uint8(span[2..], byteLen);
                length += 2;
            }
            else if (byteLen < 0x10000)
            {
                span[1] = 0xda;
                uint16(span[2..], byteLen);
                length += 3;
            }
            else if (byteLen < 0x7fffffff)
            {
                span[1] = 0xdb;
                uint32(span[2..], byteLen);
                length += 5;
            }
            else
            {
                throw new Exception("String too long");
            }

            length += byteLen;

            return length;
        }

        private static byte[] addByteToArray(byte[] byteArray, byte[] newBytes)
        {
            byte[] bytes = new byte[byteArray.Length + newBytes.Length];
            Buffer.BlockCopy(byteArray, 0, bytes, 0, byteArray.Length);
            Buffer.BlockCopy(newBytes, 0, bytes, byteArray.Length, newBytes.Length);
            return bytes;
        }

        private static byte[] uint8(byte[] bytes, int value)
        {
            return addByteToArray(bytes, new[] { (byte)(value & 255) });
        }

        private static byte[] uint16(byte[] bytes, int value)
        {
            byte[] a1 = addByteToArray(bytes, new[] { (byte)(value & 255) });
            return addByteToArray(a1, new[] { (byte)((value >> 8) & 255) });
        }

        private static byte[] uint32(byte[] bytes, int value)
        {
            int b4 = value >> 24;
            int b3 = value >> 16;
            int b2 = value >> 8;
            int b1 = value;
            byte[] a1 = addByteToArray(bytes, new[] { (byte)(b1 & 255) });
            byte[] a2 = addByteToArray(a1, new[] { (byte)(b2 & 255) });
            byte[] a3 = addByteToArray(a2, new[] { (byte)(b3 & 255) });
            return addByteToArray(a3, new[] { (byte)(b4 & 255) });
        }

        private static void uint8(Span<byte> bytes, int value)
        {
            bytes[0] = (byte)(value & 255);
        }

        private static void uint16(Span<byte> bytes, int value)
        {
            bytes[0] = (byte)(value & 255);
            bytes[1] = (byte)((value >> 8) & 255);
        }

        private static void uint32(Span<byte> bytes, int value)
        {
            bytes[0] = (byte)(value & 255);
            bytes[1] = (byte)((value >> 8) & 255);
            bytes[2] = (byte)((value >> 16) & 255);
            bytes[3] = (byte)((value >> 24) & 255);
        }
    }

}

