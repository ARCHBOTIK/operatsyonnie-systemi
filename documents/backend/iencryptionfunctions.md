# IEncryptionFunctions

## Назначение

`IEncryptionFunctions` описывает контракт статических методов для генерации ключей, вывода KEK через Argon2id и шифрования данных через AES-GCM.

## Роль в системе

Интерфейс для криптографического набора функций, реализованного классом `EncryptionFunctions`.

## Пространство имён / модуль

`SecurePassword`

Источник: `SecurePassword/SecurePassword/sql(rus)/EncryptionClasses/IEncryptionFunctions.cs`

## Зависимости

* `ArgonParameters` - параметры Argon2id.
* `OSType` - выбор набора параметров под платформу.

## Основные методы

* `GenerateSalt(int size = 16)`
  * Возвращает: `byte[]`
  * Генерирует соль.
* `GenerateKEKwArgon2id(string password, byte[] salt, OSType SystemType, int keyLength = 32)`
  * Возвращает: `byte[]`
  * Создаёт KEK из мастер-пароля и соли.
* `GetArgonParameters(OSType type)`
  * Возвращает: `ArgonParameters`
  * Возвращает параметры Argon2id для платформы.
* `GenerateDEK(int keySize = 32)`
  * Возвращает: `byte[]`
  * Генерирует ключ шифрования данных.
* `EncryptDEKwithGCM(...)`
  * Возвращает: `byte[]`
  * Шифрует DEK через AES-GCM.
* `DecryptDEK(byte[] kek, byte[] encryptedDEK)`
  * Возвращает: `byte[]`
  * Расшифровывает DEK.
* `PackAESGCMData(...)` и `UnpackAESGCMData(...)`
  * Упаковывают и распаковывают `nonce`, `tag` и шифртекст.
* `EncryptData(byte[] dek, byte[] plaintext)` и `DecryptData(byte[] dek, byte[] encryptedData)`
  * Шифруют и расшифровывают произвольные данные.

## Правила использования

* Интерфейс фиксирует контракт, а фактическая реализация находится в `EncryptionFunctions`.
* Размеры `nonce` и `tag` должны соответствовать реализации AES-GCM.
* Вызовы должны передавать корректные ключи нужной длины.
