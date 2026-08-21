using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

#if ANDROID
using Android.Content;
using Android.Net.Wifi;
#endif

namespace SecurePassword;

public sealed class NetworkService
{
    public const int SyncPort = 50555;
    private static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DefaultHandshakeTimeout = TimeSpan.FromSeconds(30);

    public event Action<string>? StatusChanged;

    public string? GetLocalIpAddress()
    {
        string? networkInterfaceAddress = GetLocalIpAddressFromNetworkInterfaces();
        if (!string.IsNullOrWhiteSpace(networkInterfaceAddress))
            return networkInterfaceAddress;

#if ANDROID
        return GetAndroidWifiIpAddress();
#else
        return null;
#endif
    }

    public async Task<byte[]> StartReceiverFlowAsync(PairingSecret pairingSecret, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pairingSecret);

        if (pairingSecret.IsExpired)
            throw new InvalidOperationException("Код сопряжения истёк.");

        EnsureNetworkAvailable();

        TcpListener? listener = null;

        try
        {
            listener = new TcpListener(IPAddress.Any, SyncPort);
            listener.Start(1);
            StatusChanged?.Invoke("Ожидание подключения устройства...");

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts.CancelAfter(pairingSecret.ExpiresAt - DateTimeOffset.UtcNow);

            using var stopRegistration = linkedCts.Token.Register(() =>
            {
                try { listener.Stop(); } catch { }
            });

            using TcpClient client = await listener.AcceptTcpClientAsync(linkedCts.Token);
            StatusChanged?.Invoke("Устройство подключилось. Проверка аутентификации...");

            await using NetworkStream stream = client.GetStream();

            // Stage 1: Generate SessionId and ReceiverNonce
            byte[] sessionId = new byte[P2PProtocol.SessionIdLength];
            byte[] receiverNonce = new byte[P2PProtocol.NonceLength];
            RandomNumberGenerator.Fill(sessionId);
            RandomNumberGenerator.Fill(receiverNonce);

            // Message 1: Send Init (SessionId, ReceiverNonce)
            await P2PProtocol.WriteMessageAsync(stream, P2PMessageType.Init, sessionId, receiverNonce, linkedCts.Token);

            // Message 2: Read AuthRequest (SessionId, SenderNonce (32) || SenderProof (32))
            var (msgType2, recvSessionId2, authReqPayload) = await P2PProtocol.ReadMessageAsync(
                stream, P2PMessageType.AuthRequest, linkedCts.Token);

            if (!CryptographicOperations.FixedTimeEquals(sessionId, recvSessionId2))
                throw new InvalidDataException("Session ID mismatch in AuthRequest.");

            if (authReqPayload.Length != P2PProtocol.NonceLength + P2PProtocol.MacLength)
                throw new InvalidDataException("Invalid AuthRequest payload length.");

            byte[] senderNonce = authReqPayload[..P2PProtocol.NonceLength];
            byte[] senderProof = authReqPayload[P2PProtocol.NonceLength..];

            // Derive Keys
            byte[] secretBytes = pairingSecret.GetSecretBytes();
            byte[] authKey;
            byte[] transportKey;
            try
            {
                P2PProtocol.DeriveSessionKeys(secretBytes, sessionId, receiverNonce, senderNonce, out authKey, out transportKey);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(secretBytes);
            }

            try
            {
                // Verify SenderProof
                byte[] expectedSenderProof = P2PProtocol.ComputeSenderProof(authKey, sessionId, receiverNonce, senderNonce);
                bool senderValid = CryptographicOperations.FixedTimeEquals(expectedSenderProof, senderProof);
                CryptographicOperations.ZeroMemory(expectedSenderProof);

                if (!senderValid)
                {
                    throw new CryptographicException("Ошибка взаимной аутентификации: неверный код сопряжения.");
                }

                // Message 3: Send AuthResponse (ReceiverProof)
                byte[] receiverProof = P2PProtocol.ComputeReceiverProof(authKey, sessionId, receiverNonce, senderNonce);
                await P2PProtocol.WriteMessageAsync(stream, P2PMessageType.AuthResponse, sessionId, receiverProof, linkedCts.Token);

                StatusChanged?.Invoke("Аутентификация успешна. Приём зашифрованных данных...");

                // Message 4: Read VaultPayload
                var (msgType4, recvSessionId4, encryptedPayload) = await P2PProtocol.ReadMessageAsync(
                    stream, P2PMessageType.VaultPayload, linkedCts.Token);

                if (!CryptographicOperations.FixedTimeEquals(sessionId, recvSessionId4))
                    throw new InvalidDataException("Session ID mismatch in VaultPayload.");

                // Decrypt Payload
                byte[] decryptedArchive = P2PProtocol.DecryptPayload(
                    encryptedPayload, transportKey, sessionId, receiverNonce, senderNonce);

                StatusChanged?.Invoke("Данные успешно получены и расшифрованы.");
                return decryptedArchive;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(authKey);
                CryptographicOperations.ZeroMemory(transportKey);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SocketException exception) when (exception.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            throw new InvalidOperationException("Порт синхронизации уже занят. Закройте другой приём базы и попробуйте снова.", exception);
        }
        catch (SocketException exception)
        {
            throw new InvalidOperationException($"Не удалось открыть TCP-приём: {exception.Message}", exception);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException("Соединение было разорвано во время обмена.", exception);
        }
        finally
        {
            try { listener?.Stop(); } catch { }
        }
    }

    public async Task SendVaultFlowAsync(
        string ip,
        string rawPairingCode,
        byte[] vaultArchiveBytes,
        CancellationToken cancellationToken = default)
    {
        EnsureNetworkAvailable();

        if (string.IsNullOrWhiteSpace(ip))
            throw new ArgumentException("IP-адрес получателя не указан.", nameof(ip));

        if (string.IsNullOrWhiteSpace(rawPairingCode))
            throw new ArgumentException("Код сопряжения не указан.", nameof(rawPairingCode));

        ArgumentNullException.ThrowIfNull(vaultArchiveBytes);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(DefaultHandshakeTimeout);

        try
        {
            using var client = new TcpClient();
            StatusChanged?.Invoke($"Подключение к {ip.Trim()}...");
            await client.ConnectAsync(ip.Trim(), SyncPort, timeoutCts.Token);
            StatusChanged?.Invoke("Соединение установлено. Выполнение аутентификации...");

            await using NetworkStream stream = client.GetStream();

            // Message 1: Read Init (SessionId, ReceiverNonce)
            var (msgType1, sessionId, receiverNonce) = await P2PProtocol.ReadMessageAsync(
                stream, P2PMessageType.Init, timeoutCts.Token);

            if (receiverNonce.Length != P2PProtocol.NonceLength)
                throw new InvalidDataException("Invalid ReceiverNonce length.");

            // Generate SenderNonce
            byte[] senderNonce = new byte[P2PProtocol.NonceLength];
            RandomNumberGenerator.Fill(senderNonce);

            // Derive Keys
            byte[] secretBytes = Encoding.UTF8.GetBytes(rawPairingCode);
            byte[] authKey;
            byte[] transportKey;
            try
            {
                P2PProtocol.DeriveSessionKeys(secretBytes, sessionId, receiverNonce, senderNonce, out authKey, out transportKey);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(secretBytes);
            }

            try
            {
                // Compute SenderProof
                byte[] senderProof = P2PProtocol.ComputeSenderProof(authKey, sessionId, receiverNonce, senderNonce);
                byte[] authReqPayload = new byte[P2PProtocol.NonceLength + P2PProtocol.MacLength];
                Buffer.BlockCopy(senderNonce, 0, authReqPayload, 0, P2PProtocol.NonceLength);
                Buffer.BlockCopy(senderProof, 0, authReqPayload, P2PProtocol.NonceLength, P2PProtocol.MacLength);

                // Message 2: Send AuthRequest
                await P2PProtocol.WriteMessageAsync(stream, P2PMessageType.AuthRequest, sessionId, authReqPayload, timeoutCts.Token);

                // Message 3: Read AuthResponse (ReceiverProof)
                var (msgType3, recvSessionId3, receiverProof) = await P2PProtocol.ReadMessageAsync(
                    stream, P2PMessageType.AuthResponse, timeoutCts.Token);

                if (!CryptographicOperations.FixedTimeEquals(sessionId, recvSessionId3))
                    throw new InvalidDataException("Session ID mismatch in AuthResponse.");

                // Verify ReceiverProof
                byte[] expectedReceiverProof = P2PProtocol.ComputeReceiverProof(authKey, sessionId, receiverNonce, senderNonce);
                bool receiverValid = CryptographicOperations.FixedTimeEquals(expectedReceiverProof, receiverProof);
                CryptographicOperations.ZeroMemory(expectedReceiverProof);

                if (!receiverValid)
                {
                    throw new CryptographicException("Ошибка аутентификации принимающей стороны: код сопряжения неверен.");
                }

                StatusChanged?.Invoke("Аутентификация успешна. Шифрование и отправка базы...");

                // Message 4: Encrypt and Send VaultPayload
                byte[] encryptedPayload = P2PProtocol.EncryptPayload(
                    vaultArchiveBytes, transportKey, sessionId, receiverNonce, senderNonce);

                await P2PProtocol.WriteMessageAsync(stream, P2PMessageType.VaultPayload, sessionId, encryptedPayload, timeoutCts.Token);
                StatusChanged?.Invoke("База данных успешно передана.");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(authKey);
                CryptographicOperations.ZeroMemory(transportKey);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Превышено время ожидания ответа от второго устройства.");
        }
        catch (SocketException exception)
        {
            throw new InvalidOperationException($"Не удалось подключиться к {ip.Trim()}:{SyncPort}. Проверьте адрес и сеть.", exception);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException("Соединение было разорвано во время отправки данных.", exception);
        }
    }

    private static void EnsureNetworkAvailable()
    {
        var networkAccess = Connectivity.Current.NetworkAccess;
        if (networkAccess is not NetworkAccess.Local and not NetworkAccess.Internet)
            throw new InvalidOperationException("Нет подключения к локальной сети.");
    }

    private static string? GetLocalIpAddressFromNetworkInterfaces()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(IsUsableLanInterface)
            .SelectMany(networkInterface => GetPrivateLanAddresses(networkInterface)
                .Select(address => new
                {
                    Address = address,
                    Interface = networkInterface
                }))
            .OrderByDescending(candidate => candidate.Interface.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
            .ThenByDescending(candidate => candidate.Interface.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
            .Select(candidate => candidate.Address.ToString())
            .FirstOrDefault();
    }

    private static bool IsUsableLanInterface(NetworkInterface networkInterface)
    {
        if (networkInterface.OperationalStatus != OperationalStatus.Up)
            return false;

        if (networkInterface.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            return false;

        string name = networkInterface.Name.ToLowerInvariant();
        string description = networkInterface.Description.ToLowerInvariant();

        return !VirtualInterfaceNameParts.Any(part => name.Contains(part) || description.Contains(part));
    }

    private static IEnumerable<IPAddress> GetPrivateLanAddresses(NetworkInterface networkInterface)
    {
        return networkInterface.GetIPProperties()
            .UnicastAddresses
            .Select(information => information.Address)
            .Where(address => address.AddressFamily == AddressFamily.InterNetwork)
            .Where(IsPrivateIpv4Address);
    }

    private static bool IsPrivateIpv4Address(IPAddress address)
    {
        byte[] bytes = address.GetAddressBytes();
        return bytes[0] == 10 ||
               (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
               (bytes[0] == 192 && bytes[1] == 168);
    }

#if ANDROID
    private static string? GetAndroidWifiIpAddress()
    {
        try
        {
            var wifiManager = Android.App.Application.Context.GetSystemService(Context.WifiService) as WifiManager;
            int ipAddress = wifiManager?.ConnectionInfo?.IpAddress ?? 0;
            if (ipAddress == 0)
                return null;

            byte[] bytes = BitConverter.GetBytes(ipAddress);
            return new IPAddress(bytes).ToString();
        }
        catch
        {
            return null;
        }
    }
#endif

    private static readonly string[] VirtualInterfaceNameParts =
    [
        "vpn", "tun", "tun0", "utun", "wintun", "tap", "ppp", "loopback"
    ];
}
