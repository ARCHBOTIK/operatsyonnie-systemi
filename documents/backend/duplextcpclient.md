# DuplexTcpClient

`DuplexTcpClient` - тонкая обертка над `TcpClient` для двустороннего обмена пакетами.

## Назначение

Класс используется как TCP-клиент: подключается к узлу, отправляет один пакет данных и может принять ответный пакет по тому же соединению.

## Поля

- `_tcpClient` - текущее TCP-соединение;
- `_stream` - сетевой поток текущего соединения.

## Свойства

- `IsConnected` - возвращает `true`, если есть подключенный `TcpClient` и открытый поток.

## Методы

### ConnectAsync

`ConnectAsync(string host, int port, CancellationToken token = default)` закрывает старое соединение, создает новый `TcpClient`, подключается к указанному адресу и сохраняет `NetworkStream`.

Адрес и порт передаются снаружи, в классе нет зашитых значений.

### SendDataAsync

`SendDataAsync(byte[] data, CancellationToken token = default)` отправляет данные через `PacketProtocol.WritePacketAsync()`.

Если соединение не установлено, выбрасывается `InvalidOperationException`.

### ReceiveDataAsync

`ReceiveDataAsync(CancellationToken token = default)` читает пакет через `PacketProtocol.ReadPacketAsync()`.

Если соединение не установлено, выбрасывается `InvalidOperationException`.

### Close

Закрывает поток и TCP-клиент, затем обнуляет поля. Ошибки закрытия подавляются.

### Dispose

Вызывает `Close()`.
