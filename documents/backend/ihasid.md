# IHasID

`IHasID` - интерфейс для моделей с идентификатором.

## Назначение

Интерфейс нужен generic-репозиториям, чтобы искать, добавлять, обновлять и удалять записи по `Id`.

## Свойства

- `Id` - идентификатор записи.

## Источники

В проекте есть два файла с интерфейсом:

- `SecurePassword/SecurePassword/sql(rus)/ItemClasses/IHasID.cs` - интерфейс в namespace `SecurePassword`;
- `SecurePassword/SecurePassword/Backend(Art)/Data/IHasID.cs` - интерфейс без namespace.

Основной код репозиториев и моделей в namespace `SecurePassword` использует вариант `SecurePassword.IHasID`.
