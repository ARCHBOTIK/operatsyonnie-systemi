# Generator

`Generator.razor` - страница генерации паролей.

## Назначение

Страница позволяет выбрать наборы символов, длину пароля, сгенерировать пароль, оценить его стойкость и скопировать результат в буфер обмена.

## Состояние

- `generatedPassword` - текущий текст в области результата;
- `passwordLength` - длина пароля, по умолчанию `12`;
- `includeDigits` - использовать цифры;
- `includeLowercase` - использовать строчные буквы;
- `includeUppercase` - использовать прописные буквы;
- `includeSpecial` - использовать специальные символы;
- `copied` - показывает уведомление о копировании.

Свойство `PasswordLength` ограничивает ввод диапазоном от `4` до `255`.

## Генерация

`GeneratePasswordValue()` вызывает:

`PasswordGenerator.GeneratePassword(includeLowercase, includeUppercase, includeDigits, includeSpecial, passwordLength)`.

Если не выбран ни один тип символов, исключение `ArgumentException` выводится как текст результата.

## Копирование

`CopyPassword()` копирует пароль через `Clipboard.SetTextAsync()`, но только если уже был сгенерирован реальный пароль. Начальное сообщение и сообщение валидации не копируются.

После копирования на 1.5 секунды показывается toast.

## Оценка стойкости

Блок стойкости отображается только после генерации пароля. Для расчета используются методы `PasswordValidator`:

- `CalculateCryptographicStrength`;
- `CalculateStrengthPercentage`;
- `CalculateEntropyBits`.

CSS-класс шкалы выбирается по уровню стойкости: слабый, нормальный или хороший.
