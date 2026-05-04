using System.Net;
using System.Net.Sockets;

namespace SecurePassword;

public static class PacketProtocol
{
    public const int MaxDataLength = 10_000_000;

    public static async Task WritePacketAsync(NetworkStream stream, byte[] data, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(data);

        int length = IPAddress.HostToNetworkOrder(data.Length);
        byte[] lengthBytes = BitConverter.GetBytes(length);

        await stream.WriteAsync(lengthBytes, token);
        await stream.WriteAsync(data, token);
        await stream.FlushAsync(token);
    }

    public static async Task<byte[]> ReadPacketAsync(NetworkStream stream, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        byte[] lengthBytes = await ReadExactAsync(stream, 4, token);
        int length = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(lengthBytes));

        if (length <= 0 || length > MaxDataLength)
            throw new InvalidOperationException("Invalid packet size.");

        return await ReadExactAsync(stream, length, token);
    }

    public static async Task<byte[]> ReadExactAsync(NetworkStream stream, int size, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (size < 0)
            throw new ArgumentOutOfRangeException(nameof(size));

        byte[] buffer = new byte[size];
        int read = 0;

        while (read < size)
        {
            int chunk = await stream.ReadAsync(buffer.AsMemory(read, size - read), token);
            if (chunk == 0)
                throw new IOException("Disconnected.");

            read += chunk;
        }

        return buffer;
    }
}
