# NetworkService

`NetworkService` - низкоуровневый сервис TCP-обмена для синхронизации.

## Назначение

Класс определяет локальный LAN IPv4-адрес, принимает один пакет по TCP и отправляет один пакет на другое устройство. Для формата сообщений используется `PacketProtocol`.

## Константы и события

- `SyncPort = 50555` - стандартный порт синхронизации;
- `StatusChanged` - событие с текстом текущего сетевого состояния.

## Получение IP

`GetLocalIpAddress()` сначала ищет адрес через `NetworkInterface.GetAllNetworkInterfaces()`.

Подходящими считаются активные Wi-Fi/Ethernet интерфейсы с приватным IPv4-адресом. Loopback, tunnel и типичные VPN-интерфейсы отфильтровываются.

На Android при отсутствии адреса через `NetworkInterface` используется Wi-Fi API.

## Прием

`StartReceiverAsync(int port, CancellationToken cancellationToken)`:

1. Проверяет доступность сети.
2. Проверяет диапазон порта.
3. Открывает `TcpListener` на `IPAddress.Any`.
4. Ждет одного клиента.
5. Читает один пакет через `PacketProtocol.ReadPacketAsync()`.
6. Останавливает listener в `finally`.

Есть перегрузка `StartReceiverAsync(int port)` без токена отмены.

## Отправка

`SendAsync(string ip, int port, byte[] data, CancellationToken cancellationToken)`:

1. Проверяет доступность сети и порт.
2. Проверяет IP и данные.
3. Создает `TcpClient`.
4. Подключается с таймаутом 10 секунд.
5. Отправляет пакет через `PacketProtocol.WritePacketAsync()`.

Есть перегрузка `SendAsync(string ip, int port, byte[] data)` без токена отмены.

## Высокоуровневые сценарии

- `ReceiveFlow()` получает локальный IP, сообщает статус и принимает пакет на `SyncPort`;
- `SendFlow(string ip, byte[] data)` сообщает статус и отправляет пакет на `SyncPort`.

## Ошибки

Сетевые исключения преобразуются в понятные `InvalidOperationException` или `TimeoutException`. Отмена через `CancellationToken` пробрасывается как `OperationCanceledException`.
