using System.Net;
using System.Net.Sockets;

namespace SecurePassword;

public sealed class TcpServer
{
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private bool _isRunning;

    private long _sentPackets;
    private long _receivedPackets;

    public long SentPackets => Interlocked.Read(ref _sentPackets);
    public long ReceivedPackets => Interlocked.Read(ref _receivedPackets);
    public bool IsRunning => _isRunning;

    public event Action<byte[]>? DataReceived;
    public event Action? ClientConnected;
    public event Action? ClientDisconnected;

    public Task StartAsync(string ip, int port)
    {
        if (_isRunning)
            return Task.CompletedTask;

        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Parse(ip), port);
        _listener.Start();
        _isRunning = true;
        _ = AcceptLoopAsync(_cts.Token);

        return Task.CompletedTask;
    }

    public void Stop()
    {
        if (!_isRunning)
            return;

        _isRunning = false;
        _cts?.Cancel();

        try
        {
            _listener?.Stop();
        }
        catch
        {
        }
    }

    public async Task SendAsync(TcpClient client, byte[] data, CancellationToken token = default)
    {
        using var stream = client.GetStream();
        await PacketProtocol.WritePacketAsync(stream, data, token);
        Interlocked.Increment(ref _sentPackets);
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        if (_listener is null)
            return;

        try
        {
            while (!token.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(token);
                ClientConnected?.Invoke();
                _ = ReceiveLoopAsync(client, token);
            }
        }
        catch
        {
        }
    }

    private async Task ReceiveLoopAsync(TcpClient client, CancellationToken token)
    {
        try
        {
            using var stream = client.GetStream();

            while (!token.IsCancellationRequested)
            {
                byte[] data = await PacketProtocol.ReadPacketAsync(stream, token);
                Interlocked.Increment(ref _receivedPackets);
                DataReceived?.Invoke(data);
            }
        }
        catch
        {
        }
        finally
        {
            try
            {
                client.Close();
            }
            catch
            {
            }

            ClientDisconnected?.Invoke();
        }
    }
}
