# FormDataProcessor

`FormDataProcessor` - вспомогательный обработчик CRUD-операций поверх `SecureRepository<T>`.

## Назначение

Класс инициализирует `keyManager`, создает или загружает ключевой файл и кэширует репозитории по типу записи.

В текущем UI основной путь работы идет напрямую через `SecureRepository<T>`, поэтому этот класс выглядит как обертка для старого или вспомогательного сценария.

## Поля

- `_keyManager` - менеджер ключа, создается в `Initialize()`;
- `_isInitialized` - признак успешной инициализации;
- `_repositories` - словарь `Dictionary<Type, object>` с репозиториями по типам.

## Инициализация

`Initialize(string password, bool createNew = false)` создает `keyManager("master.key")`.

Если `createNew == true`, вызывается `CreateKeyFile(password)`, иначе `LoadKeyFile(password)`. При успехе возвращается `true`, при исключении ошибка пишется в консоль и возвращается `false`.

## Получение репозитория

`GetRepository<T>(string filename)` требует предварительный вызов `Initialize()`. Если репозиторий для типа `T` еще не создан, он создается как `SecureRepository<T>(filename, _keyManager)` и сохраняется в словаре.

## CRUD-методы

- `AddRecord<T>(T record, string filename)` - добавляет запись и сохраняет файл;
- `UpdateRecord<T>(T record, string filename)` - обновляет запись и сохраняет файл;
- `DeleteRecord<T>(int id, string filename)` - удаляет запись и сохраняет файл при успешном удалении;
- `GetRecordById<T>(int id, string filename)` - возвращает запись по ID или `null`;
- `GetAllRecords<T>(string filename)` - возвращает список записей или пустой список при ошибке;
- `RecordExists<T>(int id, string filename)` - проверяет наличие записи;
- `SortRecord<T>(string filename, Func<T, object> keySelector, bool ascending = true)` - сортирует список записей;
- `SaveAllChanges()` - вызывает `Save()` у всех созданных репозиториев через reflection.

## Ограничения

Тип `T` должен быть ссылочным типом, иметь конструктор без параметров и реализовывать `IHasID`.

Класс использует файл ключа `master.key`, тогда как основной код приложения работает с `keys.dat`.
