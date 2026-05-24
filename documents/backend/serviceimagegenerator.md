# ServiceImageGenerator

`ServiceImageGenerator` - генератор и поставщик иконок сервисов.

## Назначение

Класс возвращает готовую иконку для известных сервисов или генерирует PNG-иконку с буквами сервиса на градиентном фоне.

## Известные сервисы

`KnownIcons` сопоставляет ключевые слова с локальными изображениями:

- `yandex` -> `/passwords/yandex.png`;
- `vk`, `vkontakte` -> `/passwords/vk.jpg`;
- `sber`, `sberbank` -> `/passwords/sber.jpg`;
- `google`, `gmail` -> `/passwords/google.webp`;
- `github` -> `/passwords/github.jpg`.

Поиск выполняется по вхождению ключа в нормализованную строку сервиса.

## Методы

### GetServiceIconPath

`GetServiceIconPath(string? serviceName)` оставлен как совместимый публичный метод и вызывает `GetServiceIconSource(serviceName)`.

### GetServiceIconSource

`GetServiceIconSource(string? serviceName, string? fallbackText = null)`:

1. Строит строку поиска из `serviceName` или `fallbackText`.
2. Возвращает локальную иконку, если сервис известен.
3. Иначе генерирует PNG через SkiaSharp.
4. Возвращает результат как `data:image/png;base64,...`.

### GetServiceIconWithColors

`GetServiceIconWithColors(string? serviceName, string? fallbackText = null)` возвращает путь или data URI и две hex-строки цветов, рассчитанные из того же ключа.

## Генерация

`GenerateServiceImage()` создает изображение 200x200 с округленным прямоугольником, линейным градиентом и белыми буквами по центру.

Буквы выбираются через `GetDisplayLetters()`:

- для нескольких слов берутся первые буквы первых двух частей;
- для одного слова берутся первые два буквенно-цифровых символа;
- при пустом тексте используется `?`.

Цвета строятся детерминированно из хэша строки, поэтому один и тот же сервис получает одинаковую fallback-иконку.
