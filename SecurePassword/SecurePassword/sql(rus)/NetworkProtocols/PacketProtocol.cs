using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SecurePassword.SQL_Rus_.NetworkProtocols
{
    internal class PacketProtocol
    {
        public const int MAX_DATA_LENGTH = 10000000;
        public static async Task WritePacketAsync(NetworkStream stream, byte[] data, CancellationToken token = default)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (data == null) throw new ArgumentNullException(nameof(data));

            int length = IPAddress.HostToNetworkOrder(data.Length);
            byte[] lengthBytes = BitConverter.GetBytes(length);

            await stream.WriteAsync(lengthBytes, 0, 4, token);
            await stream.WriteAsync(data, 0, data.Length, token);
            await stream.FlushAsync(token);
        }

        public static async Task<byte[]> ReadPacketAsync(NetworkStream stream, CancellationToken token = default)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            byte[] lengthBytes = await ReadExactAsync(stream, 4, token);
            int length = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(lengthBytes));

            if (length <= 0 || length > MAX_DATA_LENGTH) throw new Exception("Invalid packet size!");

            return await ReadExactAsync(stream, length, token);
        }

        public static async Task<byte[]> ReadExactAsync(NetworkStream stream, int size, CancellationToken token = default)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (size < 0) throw new ArgumentOutOfRangeException(nameof(size));

            byte[] buffer = new byte[size];
            int read = 0;

            while (read < size)
            {
                int r = await stream.ReadAsync(buffer, read, size - read, token);
                if (r == 0) throw new Exception("Disconnected!");
                read += r;
            }

            return buffer;
        }
    }
}
