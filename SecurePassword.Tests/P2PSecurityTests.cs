using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace SecurePassword.Tests;

public class P2PSecurityTests
{
    private static (byte[] SessionId, byte[] RecvNonce, byte[] SendNonce) GenerateNonces()
    {
        byte[] sessionId = new byte[16];
        byte[] recvNonce = new byte[32];
        byte[] sendNonce = new byte[32];
        RandomNumberGenerator.Fill(sessionId);
        RandomNumberGenerator.Fill(recvNonce);
        RandomNumberGenerator.Fill(sendNonce);
        return (sessionId, recvNonce, sendNonce);
    }

    [Fact]
    public void Test01_ValidPairingSecret_DerivesIdenticalKeysAndValidProofs()
    {
        using var secret = PairingSecret.Generate();
        var (sessionId, recvNonce, sendNonce) = GenerateNonces();

        // Both sides derive keys with the same secret
        P2PProtocol.DeriveSessionKeys(secret.GetSecretBytes(), sessionId, recvNonce, sendNonce, out var senderAuthKey, out var senderTransportKey);
        P2PProtocol.DeriveSessionKeys(secret.GetSecretBytes(), sessionId, recvNonce, sendNonce, out var receiverAuthKey, out var receiverTransportKey);

        Assert.Equal(senderAuthKey, receiverAuthKey);
        Assert.Equal(senderTransportKey, receiverTransportKey);

        // Sender proof computed and verified by receiver
        byte[] senderProof = P2PProtocol.ComputeSenderProof(senderAuthKey, sessionId, recvNonce, sendNonce);
        byte[] expectedSenderProof = P2PProtocol.ComputeSenderProof(receiverAuthKey, sessionId, recvNonce, sendNonce);
        Assert.True(CryptographicOperations.FixedTimeEquals(senderProof, expectedSenderProof));

        // Receiver proof computed and verified by sender
        byte[] receiverProof = P2PProtocol.ComputeReceiverProof(receiverAuthKey, sessionId, recvNonce, sendNonce);
        byte[] expectedReceiverProof = P2PProtocol.ComputeReceiverProof(senderAuthKey, sessionId, recvNonce, sendNonce);
        Assert.True(CryptographicOperations.FixedTimeEquals(receiverProof, expectedReceiverProof));
    }

    [Fact]
    public void Test02_WrongPairingSecret_DerivesDifferentKeysAndFailsAuth()
    {
        using var correctSecret = PairingSecret.Generate();
        using var wrongSecret = PairingSecret.Generate();
        var (sessionId, recvNonce, sendNonce) = GenerateNonces();

        P2PProtocol.DeriveSessionKeys(wrongSecret.GetSecretBytes(), sessionId, recvNonce, sendNonce, out var attackerAuthKey, out _);
        P2PProtocol.DeriveSessionKeys(correctSecret.GetSecretBytes(), sessionId, recvNonce, sendNonce, out var receiverAuthKey, out _);

        byte[] attackerProof = P2PProtocol.ComputeSenderProof(attackerAuthKey, sessionId, recvNonce, sendNonce);
        byte[] expectedProof = P2PProtocol.ComputeSenderProof(receiverAuthKey, sessionId, recvNonce, sendNonce);

        Assert.False(CryptographicOperations.FixedTimeEquals(attackerProof, expectedProof));
    }

    [Fact]
    public void Test03_TamperedAuthenticationMac_FailsVerification()
    {
        using var secret = PairingSecret.Generate();
        var (sessionId, recvNonce, sendNonce) = GenerateNonces();

        P2PProtocol.DeriveSessionKeys(secret.GetSecretBytes(), sessionId, recvNonce, sendNonce, out var authKey, out _);
        byte[] senderProof = P2PProtocol.ComputeSenderProof(authKey, sessionId, recvNonce, sendNonce);

        // Tamper 1 bit in proof
        senderProof[0] ^= 0x01;

        byte[] expectedProof = P2PProtocol.ComputeSenderProof(authKey, sessionId, recvNonce, sendNonce);
        Assert.False(CryptographicOperations.FixedTimeEquals(senderProof, expectedProof));
    }

    [Fact]
    public void Test04_MitmTamperedHandshakeNonce_FailsVerification()
    {
        using var secret = PairingSecret.Generate();
        var (sessionId, recvNonce, sendNonce) = GenerateNonces();

        P2PProtocol.DeriveSessionKeys(secret.GetSecretBytes(), sessionId, recvNonce, sendNonce, out var senderAuthKey, out _);
        byte[] senderProof = P2PProtocol.ComputeSenderProof(senderAuthKey, sessionId, recvNonce, sendNonce);

        // MITM tampers sendNonce in transit
        byte[] tamperedSendNonce = (byte[])sendNonce.Clone();
        tamperedSendNonce[5] ^= 0xFF;

        P2PProtocol.DeriveSessionKeys(secret.GetSecretBytes(), sessionId, recvNonce, tamperedSendNonce, out var receiverAuthKey, out _);
        byte[] expectedProof = P2PProtocol.ComputeSenderProof(receiverAuthKey, sessionId, recvNonce, tamperedSendNonce);

        Assert.False(CryptographicOperations.FixedTimeEquals(senderProof, expectedProof));
    }

    [Fact]
    public void Test05_TamperedEncryptedPayload_ThrowsAuthenticationException()
    {
        using var secret = PairingSecret.Generate();
        var (sessionId, recvNonce, sendNonce) = GenerateNonces();

        P2PProtocol.DeriveSessionKeys(secret.GetSecretBytes(), sessionId, recvNonce, sendNonce, out _, out var transportKey);

        byte[] plaintext = Encoding.UTF8.GetBytes("Sensitive Vault Archive Contents");
        byte[] encrypted = P2PProtocol.EncryptPayload(plaintext, transportKey, sessionId, recvNonce, sendNonce);

        // Tamper ciphertext byte
        encrypted[^1] ^= 0xAA;

        Assert.Throws<AuthenticationTagMismatchException>(() =>
        {
            P2PProtocol.DecryptPayload(encrypted, transportKey, sessionId, recvNonce, sendNonce);
        });
    }

    [Fact]
    public void Test06_TamperedGcmTag_ThrowsAuthenticationException()
    {
        using var secret = PairingSecret.Generate();
        var (sessionId, recvNonce, sendNonce) = GenerateNonces();

        P2PProtocol.DeriveSessionKeys(secret.GetSecretBytes(), sessionId, recvNonce, sendNonce, out _, out var transportKey);

        byte[] plaintext = Encoding.UTF8.GetBytes("Test Payload");
        byte[] encrypted = P2PProtocol.EncryptPayload(plaintext, transportKey, sessionId, recvNonce, sendNonce);

        // Nonce is 12 bytes; Tag is bytes 12..27
        encrypted[15] ^= 0x01; // Tamper tag

        Assert.Throws<AuthenticationTagMismatchException>(() =>
        {
            P2PProtocol.DecryptPayload(encrypted, transportKey, sessionId, recvNonce, sendNonce);
        });
    }

    [Fact]
    public void Test07_ReplayWithOldSessionId_RejectedByAadCheck()
    {
        using var secret = PairingSecret.Generate();
        var (sessionId1, recvNonce1, sendNonce1) = GenerateNonces();
        var (sessionId2, recvNonce2, sendNonce2) = GenerateNonces();

        P2PProtocol.DeriveSessionKeys(secret.GetSecretBytes(), sessionId1, recvNonce1, sendNonce1, out _, out var transportKey1);
        P2PProtocol.DeriveSessionKeys(secret.GetSecretBytes(), sessionId2, recvNonce2, sendNonce2, out _, out var transportKey2);

        byte[] plaintext = Encoding.UTF8.GetBytes("Valid Session 1 Payload");
        byte[] encryptedSession1 = P2PProtocol.EncryptPayload(plaintext, transportKey1, sessionId1, recvNonce1, sendNonce1);

        // Try decrypting Session 1 payload inside Session 2 context
        Assert.Throws<AuthenticationTagMismatchException>(() =>
        {
            P2PProtocol.DecryptPayload(encryptedSession1, transportKey2, sessionId2, recvNonce2, sendNonce2);
        });
    }

    [Fact]
    public void Test08_ExpiredPairingSecret_ThrowsObjectDisposedOrExpiration()
    {
        var secret = PairingSecret.Generate(expirationSeconds: -1); // Already expired
        Assert.True(secret.IsExpired);

        secret.Dispose();
        Assert.Throws<ObjectDisposedException>(() => secret.GetSecretBytes());
    }

    [Fact]
    public async Task Test09_OversizedPacket_RejectedBeforeAllocation()
    {
        using var stream = new MemoryStream();
        byte[] sessionId = new byte[16];

        // Manually write packet header with length exceeding MaxPacketSize (50MB)
        byte[] header = new byte[4 + 1 + 1 + 16 + 4];
        Buffer.BlockCopy(P2PProtocol.Magic, 0, header, 0, 4);
        header[4] = P2PProtocol.Version;
        header[5] = (byte)P2PMessageType.VaultPayload;
        Buffer.BlockCopy(sessionId, 0, header, 6, 16);

        int hugeLength = IPAddress.HostToNetworkOrder(60 * 1024 * 1024); // 60 MB
        Buffer.BlockCopy(BitConverter.GetBytes(hugeLength), 0, header, 22, 4);

        stream.Write(header);
        stream.Position = 0;

        // Since ReadMessageAsync requires NetworkStream or we wrap it, let's verify via TCP loopback
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            await using var netStream = client.GetStream();
            await netStream.WriteAsync(header);
        });

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        await using var clientStream = client.GetStream();

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            await P2PProtocol.ReadMessageAsync(clientStream);
        });

        await serverTask;
        listener.Stop();
    }

    [Fact]
    public async Task Test10_InvalidProtocolMagic_RejectedImmediately()
    {
        byte[] invalidHeader = [0x42, 0x41, 0x44, 0x31, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            await using var netStream = client.GetStream();
            await netStream.WriteAsync(invalidHeader);
        });

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        await using var clientStream = client.GetStream();

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            await P2PProtocol.ReadMessageAsync(clientStream);
        });

        await serverTask;
        listener.Stop();
    }

    [Fact]
    public async Task Test11_UnsupportedProtocolVersion_Rejected()
    {
        byte[] headerWithWrongVer = [0x53, 0x50, 0x50, 0x31, 99, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            await using var netStream = client.GetStream();
            await netStream.WriteAsync(headerWithWrongVer);
        });

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        await using var clientStream = client.GetStream();

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            await P2PProtocol.ReadMessageAsync(clientStream);
        });

        await serverTask;
        listener.Stop();
    }

    [Fact]
    public async Task Test12_TimeoutAndCancellation_StopsCleanly()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        await using var clientStream = client.GetStream();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await P2PProtocol.ReadExactAsync(clientStream, 100, cts.Token);
        });

        listener.Stop();
    }
}
