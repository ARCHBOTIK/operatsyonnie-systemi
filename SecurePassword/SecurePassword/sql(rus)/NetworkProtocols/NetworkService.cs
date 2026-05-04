using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

#if ANDROID
using Android.Content;
using Android.Net.Wifi;
#endif

namespace SecurePassword;

public sealed class NetworkService
{
    public const int SyncPort = 50555;

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);
    private static readonly string[] VirtualInterfaceNameParts =
    [
        "vpn",
        "tun",
        "tun0",
        "utun",
        "wintun",
        "tap",
        "ppp",
        "loopback"
    ];

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

    public Task<byte[]> StartReceiverAsync(int port)
    {
        return StartReceiverAsync(port, CancellationToken.None);
    }

    public async Task<byte[]> StartReceiverAsync(int port, CancellationToken cancellationToken)
    {
        EnsureNetworkAvailable();
        ValidatePort(port);

        TcpListener? listener = null;

        try
        {
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start(1);
            StatusChanged?.Invoke("Ожидание подключения");

            using var stopRegistration = cancellationToken.Register(() =>
            {
                try
                {
                    listener.Stop();
                }
                catch
                {
                }
            });

            using TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken);
            await using NetworkStream stream = client.GetStream();
            return await PacketProtocol.ReadPacketAsync(stream, cancellationToken);
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
            throw new InvalidOperationException("Соединение было прервано во время приёма данных.", exception);
        }
        finally
        {
            try
            {
                listener?.Stop();
            }
            catch
            {
            }
        }
    }

    public Task SendAsync(string ip, int port, byte[] data)
    {
        return SendAsync(ip, port, data, CancellationToken.None);
    }

    public async Task SendAsync(string ip, int port, byte[] data, CancellationToken cancellationToken)
    {
        EnsureNetworkAvailable();
        ValidatePort(port);

        if (string.IsNullOrWhiteSpace(ip))
            throw new ArgumentException("IP-адрес получателя не указан.", nameof(ip));

        ArgumentNullException.ThrowIfNull(data);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ConnectTimeout);

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(ip.Trim(), port, timeoutCts.Token);
            await using NetworkStream stream = client.GetStream();
            await PacketProtocol.WritePacketAsync(stream, data, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Не удалось подключиться ко второму устройству за отведённое время.");
        }
        catch (SocketException exception)
        {
            throw new InvalidOperationException($"Не удалось подключиться к устройству {ip.Trim()}. Проверьте IP-адрес и сеть.", exception);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException("Соединение было прервано во время отправки данных.", exception);
        }
    }

    public async Task<byte[]> ReceiveFlow(CancellationToken cancellationToken = default)
    {
        string? localIp = GetLocalIpAddress();
        if (string.IsNullOrWhiteSpace(localIp))
            throw new InvalidOperationException("IP-адрес устройства не найден. Подключитесь к Wi-Fi или локальной сети.");

        StatusChanged?.Invoke($"Ожидание подключения. IP этого устройства: {localIp}");
        return await StartReceiverAsync(SyncPort, cancellationToken);
    }

    public async Task SendFlow(string ip, byte[] data, CancellationToken cancellationToken = default)
    {
        StatusChanged?.Invoke("Отправка базы данных");
        await SendAsync(ip, SyncPort, data, cancellationToken);
        StatusChanged?.Invoke("База данных отправлена");
    }

    private static void EnsureNetworkAvailable()
    {
        var networkAccess = Connectivity.Current.NetworkAccess;
        if (networkAccess is not NetworkAccess.Local and not NetworkAccess.Internet)
            throw new InvalidOperationException("Нет подключения к локальной сети.");
    }

    private static void ValidatePort(int port)
    {
        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), "Порт должен быть в диапазоне от 1 до 65535.");
    }

    private static string? GetLocalIpAddressFromNetworkInterfaces()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(IsUsableLanInterface)
            .SelectMany(networkInterface => GetPrivateLanAddresses(networkInterface)
                .Select(address => new
                {
                    Address = address,
                    Priority = GetInterfacePriority(networkInterface)
                }))
            .OrderBy(candidate => candidate.Priority)
            .Select(candidate => candidate.Address.ToString())
            .FirstOrDefault();
    }

    private static bool IsUsableLanInterface(NetworkInterface networkInterface)
    {
        if (networkInterface.OperationalStatus != OperationalStatus.Up)
            return false;

        if (networkInterface.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            return false;

        string name = networkInterface.Name ?? string.Empty;
        string description = networkInterface.Description ?? string.Empty;
        string combinedName = $"{name} {description}".ToLowerInvariant();

        if (VirtualInterfaceNameParts.Any(combinedName.Contains))
            return false;

        return networkInterface.NetworkInterfaceType is
            NetworkInterfaceType.Wireless80211 or
            NetworkInterfaceType.Ethernet or
            NetworkInterfaceType.GigabitEthernet or
            NetworkInterfaceType.FastEthernetFx or
            NetworkInterfaceType.FastEthernetT;
    }

    private static IEnumerable<IPAddress> GetPrivateLanAddresses(NetworkInterface networkInterface)
    {
        IPInterfaceProperties properties = networkInterface.GetIPProperties();

        return properties.UnicastAddresses
            .Select(address => address.Address)
            .Where(address =>
                address.AddressFamily == AddressFamily.InterNetwork &&
                !IPAddress.IsLoopback(address) &&
                IsPrivateLanAddress(address));
    }

    private static int GetInterfacePriority(NetworkInterface networkInterface)
    {
        return networkInterface.NetworkInterfaceType switch
        {
            NetworkInterfaceType.Wireless80211 => 0,
            NetworkInterfaceType.Ethernet => 1,
            NetworkInterfaceType.GigabitEthernet => 1,
            NetworkInterfaceType.FastEthernetFx => 1,
            NetworkInterfaceType.FastEthernetT => 1,
            _ => 10
        };
    }

    private static bool IsPrivateLanAddress(IPAddress address)
    {
        byte[] bytes = address.GetAddressBytes();
        return bytes[0] == 10 ||
            bytes[0] == 192 && bytes[1] == 168 ||
            bytes[0] == 172 && bytes[1] is >= 16 and <= 31;
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
            var address = new IPAddress(bytes);
            return !IPAddress.IsLoopback(address) && IsPrivateLanAddress(address)
                ? address.ToString()
                : null;
        }
        catch
        {
            return null;
        }
    }
#endif
}
