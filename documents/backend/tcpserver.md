# TcpServer

`TcpServer` - простой TCP-сервер для приема пакетных сообщений.

## Назначение

Класс открывает `TcpListener`, принимает клиентов в фоне и читает данные через `PacketProtocol`. Для каждого полученного пакета вызывается событие `DataReceived`.

## Поля

- `_listener` - активный `TcpListener`;
- `_cts` - источник отмены фонового цикла;
- `_isRunning` - признак запущенного сервера;
- `_sentPackets` - счетчик отправленных пакетов;
- `_receivedPackets` - счетчик принятых пакетов.

## Свойства

- `SentPackets` - потокобезопасное чтение счетчика отправленных пакетов;
- `ReceivedPackets` - потокобезопасное чтение счетчика принятых пакетов;
- `IsRunning` - запущен ли сервер.

## События

- `DataReceived` - вызывается после чтения пакета;
- `ClientConnected` - вызывается после подключения клиента;
- `ClientDisconnected` - вызывается после завершения обработки клиента.

## Методы

### StartAsync

`StartAsync(string ip, int port)` создает `TcpListener`, запускает его и стартует фоновый `AcceptLoopAsync()`.

Если сервер уже запущен, метод ничего не делает.

### Stop

Останавливает сервер: сбрасывает `_isRunning`, отменяет токен и останавливает listener.

### SendAsync

`SendAsync(TcpClient client, byte[] data, CancellationToken token = default)` отправляет пакет в поток переданного клиента через `PacketProtocol.WritePacketAsync()` и увеличивает счетчик отправленных пакетов.

### AcceptLoopAsync

Фоновый цикл принимает клиентов и запускает для каждого `ReceiveLoopAsync()`.

### ReceiveLoopAsync

Читает пакеты из клиента до отмены или ошибки. После каждого пакета увеличивает `ReceivedPackets` и вызывает `DataReceived`.

При завершении закрывает клиента и вызывает `ClientDisconnected`.
