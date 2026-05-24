# CardEntry

`CardEntry` - модель банковской карты.

## Назначение

Класс хранит данные карты и реализует `IHasID`, чтобы запись могла использоваться в `SecureRepository<T>`.

## Поля данных

- `Id` - идентификатор записи;
- `TitleBytes` - название карты в UTF-8;
- `CardNumberBytes` - номер карты в UTF-8;
- `CardHolderBytes` - владелец карты в UTF-8;
- `ExpiryDateBytes` - срок действия в UTF-8;
- `CvvBytes` - CVV в UTF-8;
- `BankNameBytes` - название банка в UTF-8;
- `CreatedAt` - время создания;
- `UpdatedAt` - время последнего изменения.

## Строковые свойства

- `Title`;
- `CardNumber`;
- `CardHolder`;
- `ExpiryDate`;
- `Cvv`;
- `BankName`.

Свойства преобразуют строки в массивы байтов и обратно через `Encoding.UTF8`. Если массив байтов не задан, getter возвращает пустую строку.

## Конструктор

Конструктор выставляет `CreatedAt` и `UpdatedAt` в `DateTime.UtcNow`.

## Важно

Сама модель не шифрует отдельные поля. Шифрование выполняется на уровне файла в `SecureRepository<T>`.
