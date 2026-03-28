using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;


namespace SecurePassword.SQL_Rus_.NetworkProtocols
{
    internal class TcpServer
    {
        private TcpListener _listener;
        private CancellationTokenSource _cts;
        private TcpClient _client;
        public long SentPackets => Interlocked.Read(ref _sentPackets);
        public long ReceivedPackets => Interlocked.Read(ref _receivedPackets);

        private long _sentPackets;
        private long _receivedPackets;
        public event Action<byte[]> DataReceived;
        public event Action ClientConnected;
        public event Action ClientDisconnected;
        private bool _isRunning;
        public bool IsRunning => _isRunning;

        public Task StartAsync(string ip, int port)
        {
            if (_isRunning) return Task.CompletedTask;
            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Parse(ip), port);
            _listener.Start();
            _isRunning = true;
            _ = AcceptLoop(_cts.Token);
            return Task.CompletedTask;
        }

        public void Stop()
        {
            if (!_isRunning) return;
            _isRunning = false;
            _cts?.Cancel();
            try
            {
                _listener?.Stop();
            }
            catch { }
            try
            {
                _client.Close();
            }
            catch { }
            return;
        }

        private async Task AcceptLoop(CancellationToken token)
        {
            var listener = _listener;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var client = await listener.AcceptTcpClientAsync();
                    ClientConnected?.Invoke();
                    _ = ReceiveLoop(client, token);
                }
            }
            catch { }

        }

        private async Task ReceiveLoop(System.Net.Sockets.TcpClient client, CancellationToken token)
        {
            var stream = client.GetStream();
            try
            {
                while(!token.IsCancellationRequested)
                {
                    byte[] lengthBytes = await PacketProtocol.ReadExactAsync(stream, 4, token);
                    int length = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(lengthBytes, 0));
                    if (length <= 0 || length > PacketProtocol.MAX_DATA_LENGTH) throw new Exception("Invalid packet size!");
                    byte[] data = await PacketProtocol.ReadExactAsync(stream, length, token);
                    Interlocked.Increment(ref _receivedPackets);
                    DataReceived?.Invoke(data);
                }
            }
            catch { }
            client.Close();
            ClientDisconnected.Invoke();
        }

        public async Task SendAsync(System.Net.Sockets.TcpClient client, byte[] data)
        {
            var stream = client.GetStream();
            int length = IPAddress.HostToNetworkOrder(data.Length);
            byte[] lengthBytes = BitConverter.GetBytes(length);
            await stream.WriteAsync(lengthBytes, 0, 4);
            await stream.WriteAsync(data, 0, data.Length);
            Interlocked.Increment(ref _sentPackets);
        }
    }
}
