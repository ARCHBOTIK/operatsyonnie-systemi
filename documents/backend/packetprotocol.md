# PacketProtocol

`PacketProtocol` - протокол упаковки сообщений поверх `NetworkStream`.

## Назначение

TCP передает поток байтов, поэтому класс добавляет к каждому сообщению 4-байтовый префикс длины. Это позволяет принимающей стороне прочитать ровно один пакет.

## Константы

- `MaxDataLength = 10_000_000` - максимальный размер полезной нагрузки в байтах.

## Методы

### WritePacketAsync

`WritePacketAsync(NetworkStream stream, byte[] data, CancellationToken token = default)`:

1. Проверяет `stream` и `data` на `null`.
2. Преобразует длину данных в network byte order.
3. Записывает 4 байта длины.
4. Записывает полезную нагрузку.
5. Выполняет `FlushAsync()`.

### ReadPacketAsync

`ReadPacketAsync(NetworkStream stream, CancellationToken token = default)`:

1. Читает 4 байта длины через `ReadExactAsync()`.
2. Преобразует длину из network byte order.
3. Проверяет, что длина больше 0 и не превышает `MaxDataLength`.
4. Читает полезную нагрузку указанного размера.

При некорректном размере выбрасывается `InvalidOperationException`.

### ReadExactAsync

`ReadExactAsync(NetworkStream stream, int size, CancellationToken token = default)` читает ровно `size` байт.

Если поток закрыт раньше, выбрасывается `IOException("Disconnected.")`. Если размер отрицательный, выбрасывается `ArgumentOutOfRangeException`.
