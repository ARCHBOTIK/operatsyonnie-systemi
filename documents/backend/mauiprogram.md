# MauiProgram

## Назначение

`MauiProgram` создаёт и настраивает MAUI-приложение.

## Роль в системе

Точка конфигурации приложения: подключает Blazor WebView, шрифты, отладочные инструменты, DI-сервисы, репозитории и платформенные настройки.

## Пространство имён / модуль

`SecurePassword`

Источник: `SecurePassword/SecurePassword/MauiProgram.cs`

## Зависимости

* `Microsoft.Maui` - создание приложения и регистрация сервисов.
* `Microsoft.Extensions.Logging` - отладочное логирование.
* `Velopack` - запуск обновлятора на Windows.
* `keyManager`, `MasterPasswordService`, `VaultSessionService`, `NetworkService`, `TcpBridge`.
* `SecureRepository<PasswordEntry>`, `SecureRepository<CardEntry>`, `SecureRepository<NoteEntry>`.

## Основные методы

* `CreateMauiApp()`
  * Возвращает: `MauiApp`
  * Создаёт builder, регистрирует зависимости и возвращает собранное приложение.

## Логика работы

1. На Windows запускается Velopack.
2. Создаётся `MauiAppBuilder`.
3. Подключаются приложение, шрифты и Blazor WebView.
4. В режиме `DEBUG` включаются инструменты разработчика и debug-логирование.
5. В DI регистрируются ключевые сервисы и защищённые репозитории.
6. На Android настраиваются системные цвета окна.

## Правила использования

* Новые сервисы приложения нужно регистрировать здесь.
* Пути к файлам данных должны формироваться через `FileSystem.AppDataDirectory`.
* Платформенную конфигурацию держать внутри соответствующих директив компиляции.
