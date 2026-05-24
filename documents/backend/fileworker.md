# FileWorker

`FileWorker` - вспомогательный класс для чтения и записи файлов приложения.

## Назначение

Класс работает с файлами внутри `FileSystem.AppDataDirectory`. Перед чтением и записью создает директорию приложения, если ее нет.

## Методы

### readFile

`readFile(string fileName)`:

1. Берет путь `FileSystem.AppDataDirectory`.
2. Создает директорию через `Directory.CreateDirectory()`.
3. Добавляет имя файла через `Path.Combine()`.
4. Возвращает результат `File.ReadAllBytes()`.

Если файл отсутствует, исключение `FileNotFoundException` передается вызывающему коду.

### writeFile

`writeFile(byte[] bytes, string fileName)`:

1. Берет путь `FileSystem.AppDataDirectory`.
2. Создает директорию через `Directory.CreateDirectory()`.
3. Добавляет имя файла через `Path.Combine()`.
4. Записывает данные через `File.WriteAllBytes()`.
