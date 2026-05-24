# IPasswordGenerator

`IPasswordGenerator` - внутренний интерфейс генератора паролей.

## Назначение

Интерфейс задает статический контракт, который реализует `PasswordGenerator`.

## Методы

- `GeneratePassword(bool useLowercase, bool useUppercase, bool useDigits, bool useSpecial, byte passwordLength)` - генерация пароля указанной длины;
- `GeneratePassword(bool useLowercase, bool useUppercase, bool useDigits, bool useSpecial)` - генерация пароля с длиной по умолчанию;
- `ValidatePassword(string password, bool useLowercase, bool useUppercase, bool useDigits, bool useSpecial)` - проверка наличия символов из выбранных наборов.

## Важно

Интерфейс объявлен как `internal`, поэтому доступен только внутри сборки.
