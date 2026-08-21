using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace SecurePassword;

public enum P2PMessageType : byte
{
    Init = 1,
    AuthRequest = 2,
    AuthResponse = 3,
    VaultPayload = 4
}

public static class P2PProtocol
{
    public static readonly byte[] Magic = [0x53, 0x50, 0x50, 0x31]; // 'S', 'P', 'P', '1'
    public const byte Version = 1;
    public const int MaxPacketSize = 50 * 1024 * 1024; // 50 MB max
    public const int SessionIdLength = 16;
    public const int NonceLength = 32;
    public const int MacLength = 32;
    public const int AesGcmNonceLength = 12;
    public const int AesGcmTagLength = 16;

    private static readonly byte[] SenderProofPrefix = Encoding.UTF8.GetBytes("SPP1-SenderAuth-v1");
    private static readonly byte[] ReceiverProofPrefix = Encoding.UTF8.GetBytes("SPP1-ReceiverAuth-v1");
    private static readonly byte[] HkdfInfo = Encoding.UTF8.GetBytes("SecurePassword-P2P-v1");

    public static void DeriveSessionKeys(
        byte[] pairingSecretBytes,
        byte[] sessionId,
        byte[] receiverNonce,
        byte[] senderNonce,
        out byte[] authKey,
        out byte[] transportKey)
    {
        // Salt = SessionId || ReceiverNonce || SenderNonce
        byte[] salt = new byte[sessionId.Length + receiverNonce.Length + senderNonce.Length];
        Buffer.BlockCopy(sessionId, 0, salt, 0, sessionId.Length);
        Buffer.BlockCopy(receiverNonce, 0, salt, sessionId.Length, receiverNonce.Length);
        Buffer.BlockCopy(senderNonce, 0, salt, sessionId.Length + receiverNonce.Length, senderNonce.Length);

        byte[] derived = new byte[64];
        try
        {
            HKDF.DeriveKey(HashAlgorithmName.SHA256, pairingSecretBytes, derived, salt, HkdfInfo);

            authKey = new byte[32];
            transportKey = new byte[32];
            Buffer.BlockCopy(derived, 0, authKey, 0, 32);
            Buffer.BlockCopy(derived, 32, transportKey, 0, 32);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(derived);
            CryptographicOperations.ZeroMemory(salt);
        }
    }

    public static byte[] ComputeSenderProof(byte[] authKey, byte[] sessionId, byte[] receiverNonce, byte[] senderNonce)
    {
        byte[] message = BuildHmacMessage(SenderProofPrefix, sessionId, receiverNonce, senderNonce);
        return HMACSHA256.HashData(authKey, message);
    }

    public static byte[] ComputeReceiverProof(byte[] authKey, byte[] sessionId, byte[] receiverNonce, byte[] senderNonce)
    {
        byte[] message = BuildHmacMessage(ReceiverProofPrefix, sessionId, receiverNonce, senderNonce);
        return HMACSHA256.HashData(authKey, message);
    }

    private static byte[] BuildHmacMessage(byte[] prefix, byte[] sessionId, byte[] receiverNonce, byte[] senderNonce)
    {
        byte[] message = new byte[prefix.Length + sessionId.Length + receiverNonce.Length + senderNonce.Length];
        int offset = 0;
        Buffer.BlockCopy(prefix, 0, message, offset, prefix.Length);
        offset += prefix.Length;
        Buffer.BlockCopy(sessionId, 0, message, offset, sessionId.Length);
        offset += sessionId.Length;
        Buffer.BlockCopy(receiverNonce, 0, message, offset, receiverNonce.Length);
        offset += receiverNonce.Length;
        Buffer.BlockCopy(senderNonce, 0, message, offset, senderNonce.Length);
        return message;
    }

    public static byte[] BuildAad(byte[] sessionId, byte[] receiverNonce, byte[] senderNonce, P2PMessageType messageType)
    {
        byte[] aad = new byte[Magic.Length + 1 + sessionId.Length + receiverNonce.Length + senderNonce.Length + 1];
        int offset = 0;
        Buffer.BlockCopy(Magic, 0, aad, offset, Magic.Length);
        offset += Magic.Length;
        aad[offset++] = Version;
        Buffer.BlockCopy(sessionId, 0, aad, offset, sessionId.Length);
        offset += sessionId.Length;
        Buffer.BlockCopy(receiverNonce, 0, aad, offset, receiverNonce.Length);
        offset += receiverNonce.Length;
        Buffer.BlockCopy(senderNonce, 0, aad, offset, senderNonce.Length);
        offset += senderNonce.Length;
        aad[offset] = (byte)messageType;
        return aad;
    }

    public static byte[] EncryptPayload(
        byte[] plaintext,
        byte[] transportKey,
        byte[] sessionId,
        byte[] receiverNonce,
        byte[] senderNonce)
    {
        byte[] nonce = new byte[AesGcmNonceLength];
        RandomNumberGenerator.Fill(nonce);

        byte[] tag = new byte[AesGcmTagLength];
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] aad = BuildAad(sessionId, receiverNonce, senderNonce, P2PMessageType.VaultPayload);

        using (var aesGcm = new AesGcm(transportKey, AesGcmTagLength))
        {
            aesGcm.Encrypt(nonce, plaintext, ciphertext, tag, aad);
        }

        // Serialized Encrypted Payload = Nonce (12) || Tag (16) || Ciphertext (N)
        byte[] output = new byte[AesGcmNonceLength + AesGcmTagLength + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, output, 0, AesGcmNonceLength);
        Buffer.BlockCopy(tag, 0, output, AesGcmNonceLength, AesGcmTagLength);
        Buffer.BlockCopy(ciphertext, 0, output, AesGcmNonceLength + AesGcmTagLength, ciphertext.Length);

        return output;
    }

    public static byte[] DecryptPayload(
        byte[] encryptedPayload,
        byte[] transportKey,
        byte[] sessionId,
        byte[] receiverNonce,
        byte[] senderNonce)
    {
        if (encryptedPayload.Length < AesGcmNonceLength + AesGcmTagLength)
            throw new InvalidDataException("Invalid encrypted payload size.");

        byte[] nonce = new byte[AesGcmNonceLength];
        byte[] tag = new byte[AesGcmTagLength];
        int cipherLength = encryptedPayload.Length - AesGcmNonceLength - AesGcmTagLength;
        byte[] ciphertext = new byte[cipherLength];

        Buffer.BlockCopy(encryptedPayload, 0, nonce, 0, AesGcmNonceLength);
        Buffer.BlockCopy(encryptedPayload, AesGcmNonceLength, tag, 0, AesGcmTagLength);
        Buffer.BlockCopy(encryptedPayload, AesGcmNonceLength + AesGcmTagLength, ciphertext, 0, cipherLength);

        byte[] plaintext = new byte[cipherLength];
        byte[] aad = BuildAad(sessionId, receiverNonce, senderNonce, P2PMessageType.VaultPayload);

        using (var aesGcm = new AesGcm(transportKey, AesGcmTagLength))
        {
            aesGcm.Decrypt(nonce, ciphertext, tag, plaintext, aad);
        }

        return plaintext;
    }

    public static async Task WriteMessageAsync(
        NetworkStream stream,
        P2PMessageType messageType,
        byte[] sessionId,
        byte[] payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(payload);

        if (sessionId.Length != SessionIdLength)
            throw new ArgumentException($"SessionId must be {SessionIdLength} bytes.", nameof(sessionId));

        int totalPayloadLength = payload.Length;
        if (totalPayloadLength > MaxPacketSize)
            throw new InvalidOperationException($"Packet exceeds maximum size of {MaxPacketSize} bytes.");

        // Wire format:
        // Magic (4B) | Version (1B) | Type (1B) | SessionId (16B) | PayloadLength (4B) | Payload (NB)
        int headerSize = Magic.Length + 1 + 1 + SessionIdLength + 4;
        byte[] header = new byte[headerSize];
        int offset = 0;

        Buffer.BlockCopy(Magic, 0, header, offset, Magic.Length);
        offset += Magic.Length;
        header[offset++] = Version;
        header[offset++] = (byte)messageType;
        Buffer.BlockCopy(sessionId, 0, header, offset, SessionIdLength);
        offset += SessionIdLength;

        int networkLength = IPAddress.HostToNetworkOrder(totalPayloadLength);
        Buffer.BlockCopy(BitConverter.GetBytes(networkLength), 0, header, offset, 4);

        await stream.WriteAsync(header, cancellationToken);
        if (payload.Length > 0)
        {
            await stream.WriteAsync(payload, cancellationToken);
        }
        await stream.FlushAsync(cancellationToken);
    }

    public static async Task<(P2PMessageType Type, byte[] SessionId, byte[] Payload)> ReadMessageAsync(
        NetworkStream stream,
        P2PMessageType? expectedType = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        int headerSize = Magic.Length + 1 + 1 + SessionIdLength + 4;
        byte[] header = await ReadExactAsync(stream, headerSize, cancellationToken);

        // Validate Magic
        byte[] magic = header[..Magic.Length];
        if (!magic.SequenceEqual(Magic))
            throw new InvalidDataException("Invalid P2P protocol magic.");

        // Validate Version
        byte version = header[Magic.Length];
        if (version != Version)
            throw new InvalidDataException($"Unsupported protocol version: {version}. Expected: {Version}.");

        // Message Type
        P2PMessageType type = (P2PMessageType)header[Magic.Length + 1];
        if (expectedType.HasValue && type != expectedType.Value)
            throw new InvalidDataException($"Unexpected message type: {type}. Expected: {expectedType.Value}.");

        // Session ID
        byte[] sessionId = new byte[SessionIdLength];
        Buffer.BlockCopy(header, Magic.Length + 2, sessionId, 0, SessionIdLength);

        // Payload Length
        int networkLength = BitConverter.ToInt32(header, Magic.Length + 2 + SessionIdLength);
        int payloadLength = IPAddress.NetworkToHostOrder(networkLength);

        if (payloadLength < 0 || payloadLength > MaxPacketSize)
            throw new InvalidDataException($"Invalid packet payload size: {payloadLength}. Max allowed: {MaxPacketSize}.");

        byte[] payload = payloadLength > 0
            ? await ReadExactAsync(stream, payloadLength, cancellationToken)
            : [];

        return (type, sessionId, payload);
    }

    public static async Task<byte[]> ReadExactAsync(NetworkStream stream, int size, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (size < 0)
            throw new ArgumentOutOfRangeException(nameof(size));

        byte[] buffer = new byte[size];
        int read = 0;

        while (read < size)
        {
            int chunk = await stream.ReadAsync(buffer.AsMemory(read, size - read), cancellationToken);
            if (chunk == 0)
                throw new IOException("Connection closed by peer.");

            read += chunk;
        }

        return buffer;
    }
}
