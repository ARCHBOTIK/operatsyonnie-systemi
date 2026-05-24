# PasswordGenerator

`PasswordGenerator` - генератор паролей с криптографическим источником случайности.

## Назначение

Класс создает пароль из выбранных наборов символов и гарантирует наличие хотя бы одного символа из каждого выбранного набора.

## Наборы символов

- строчные буквы: `abcdefghijklmnopqrstuvwxyz`;
- прописные буквы: `ABCDEFGHIJKLMNOPQRSTUVWXYZ`;
- цифры: `0123456789`;
- специальные символы: `!@#$%^&*()_-+=<>?`.

Длина по умолчанию для перегрузки без длины - `15`.

## Методы

### GeneratePassword

`GeneratePassword(bool useLowercase, bool useUppercase, bool useDigits, bool useSpecial, byte passwordLength)` генерирует пароль указанной длины.

`GeneratePassword(bool useLowercase, bool useUppercase, bool useDigits, bool useSpecial)` использует длину по умолчанию.

Если не выбран ни один набор символов, выбрасывается `ArgumentException`.

Длина ограничивается диапазоном от `4` до `255`.

### ValidatePassword

`ValidatePassword(string password, bool useLowercase, bool useUppercase, bool useDigits, bool useSpecial)` проверяет, что пароль содержит символы из всех включенных наборов.

## Алгоритм

1. Формируется список выбранных наборов символов.
2. В пароль добавляется по одному случайному символу из каждого выбранного набора.
3. Остальные позиции заполняются символами из объединенного набора.
4. Итоговый список перемешивается.

Для выбора символов и перемешивания используется `RandomNumberGenerator.GetInt32()`.
