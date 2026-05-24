# EncryptionFunctions

`EncryptionFunctions` - набор криптографических функций для ключей и данных.

## Назначение

Класс генерирует соль, KEK и DEK, шифрует и расшифровывает DEK, а также шифрует пользовательские данные через AES-GCM.

## Argon2id

Для генерации KEK используются две перегрузки:

- `GenerateKEKwArgon2id(string password, byte[] salt, OSType systemType, int keyLength = 32)`;
- `GenerateKEKwArgon2id(string password, byte[] salt, ArgonParameters parameters, int keyLength = 32)`.

Первая перегрузка берет параметры через `GetArgonParameters()`, вторая использует переданный объект `ArgonParameters`. Это нужно для чтения ключевых файлов с параметрами, сохраненными внутри файла.

## Параметры платформ

`GetArgonParameters(OSType type)` возвращает:

- `Windows`: память `262144`, итерации `3`, параллелизм `3`;
- `Android`: память `2048`, итерации `2`, параллелизм `1`.

Для неизвестного типа выбрасывается `ArgumentOutOfRangeException`.

## Методы

- `GenerateSalt(int size = 16)` - генерирует криптографическую соль;
- `GenerateDEK(int keySize = 32)` - генерирует случайный ключ данных;
- `EncryptDEKwithGCM(byte[] dek, byte[] kek, out byte[] nonce, out byte[] tag, int DEKsize = 32)` - шифрует DEK через AES-GCM;
- `DecryptDEK(byte[] kek, byte[] encryptedDEK)` - расшифровывает упакованный DEK;
- `PackAESGCMData(byte[] nonce, byte[] tag, byte[] ciphertext)` - объединяет `nonce + tag + ciphertext`;
- `UnpackAESGCMData(byte[] pack, out byte[] nonce, out byte[] tag, out byte[] ciphertext)` - разбирает упакованные данные;
- `EncryptData(byte[] dek, byte[] plaintext)` - шифрует произвольные данные через DEK;
- `DecryptData(byte[] dek, byte[] encryptedData)` - расшифровывает данные через DEK.

## Формат AES-GCM

Упакованные данные всегда имеют формат:

`12 байт nonce + 16 байт tag + ciphertext`.

Этот формат используется и для DEK, и для файлов с пользовательскими данными.

## Важно

Класс статeless: он не хранит ключи и не управляет файлами. Состояние DEK хранится в `keyManager`, а файлы данных обслуживаются репозиториями.
