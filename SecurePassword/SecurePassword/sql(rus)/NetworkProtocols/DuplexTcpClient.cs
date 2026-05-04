using System.Net.Sockets;

namespace SecurePassword;

public sealed class DuplexTcpClient : IDisposable
{
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;

    public bool IsConnected => _tcpClient?.Connected == true && _stream is not null;

    public async Task ConnectAsync(string host, int port, CancellationToken token = default)
    {
        Close();

        _tcpClient = new TcpClient();
        await _tcpClient.ConnectAsync(host, port, token);
        _stream = _tcpClient.GetStream();
    }

    public async Task SendDataAsync(byte[] data, CancellationToken token = default)
    {
        if (_stream is null)
            throw new InvalidOperationException("Client is not connected.");

        await PacketProtocol.WritePacketAsync(_stream, data, token);
    }

    public async Task<byte[]> ReceiveDataAsync(CancellationToken token = default)
    {
        if (_stream is null)
            throw new InvalidOperationException("Client is not connected.");

        return await PacketProtocol.ReadPacketAsync(_stream, token);
    }

    public void Close()
    {
        try
        {
            _stream?.Close();
        }
        catch
        {
        }

        try
        {
            _tcpClient?.Close();
        }
        catch
        {
        }

        _stream = null;
        _tcpClient = null;
    }

    public void Dispose()
    {
        Close();
    }
}
