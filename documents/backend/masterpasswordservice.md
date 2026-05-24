# MasterPasswordService

`MasterPasswordService` - сервис прикладных операций с мастер-паролем.

## Назначение

Сервис связывает UI с `keyManager`: проверяет наличие ключевого файла, создает мастер-пароль, выполняет вход и меняет мастер-пароль.

## Поля

- `_keyManager` - менеджер ключевого файла;
- `_keyFilePath` - путь к `keys.dat` в `FileSystem.AppDataDirectory`.

## Методы

### KeyFileExists

`KeyFileExists()` возвращает `true`, если файл `keys.dat` существует.

### CreateMasterPassword

`CreateMasterPassword(string password)` создает новый ключевой файл через `_keyManager.CreateKeyFile(password)`.

Если `keys.dat` уже существует, выбрасывается `InvalidOperationException`.

### Login

`Login(string password)` вызывает `_keyManager.LoadKeyFile(password)`, затем проверяет, что `GetDEK()` вернул непустой ключ.

Если DEK не загружен, выбрасывается `InvalidOperationException`.

### ChangeMasterPassword

`ChangeMasterPassword(string oldPassword, string newPassword)` делегирует смену пароля в `_keyManager.replaceMasterPassword(oldPassword, newPassword)`.

## Важно

Сервис не хранит мастер-пароль и не дает прямой доступ к DEK. Он только вызывает операции `keyManager`.
