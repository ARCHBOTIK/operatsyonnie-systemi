using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SecurePassword.SQL_Rus_.NetworkProtocols
{
    internal class DuplexTCPClient
    {
        private TcpClient _TcpClient;
        private NetworkStream _stream;
        private CancellationTokenSource _cts;

        public event Action<byte[]> DataReceived;

        public async Task ConnectAsync(string ip, int port)
        {
            _TcpClient = new TcpClient();
            await _TcpClient.ConnectAsync(ip, port);
            _stream = _TcpClient.GetStream();
            _cts = new CancellationTokenSource();
            _ = ReceiveLoop(_cts.Token);
        }

        public async Task SendDataAsync(byte[] data, CancellationToken token = default)
        {
            await PacketProtocol.WritePacketAsync(_stream, data, token);
        }

        private async Task ReceiveLoop(CancellationToken token)
        {
            try
            {
                while(!token.IsCancellationRequested)
                {
                    byte[] data = await PacketProtocol.ReadPacketAsync(_stream, token);
                    DataReceived?.Invoke(data);
                }
            }
            catch
            {

            }
        }

        public void Close()
        {
            try { _cts?.Cancel(); } catch { }
            try { _stream?.Close(); } catch { }
            try { _TcpClient?.Close(); } catch { }
        }
    }
}
