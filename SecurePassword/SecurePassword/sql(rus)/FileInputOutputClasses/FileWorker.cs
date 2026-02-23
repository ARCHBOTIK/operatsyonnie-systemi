namespace SecurePassword;

internal class FileWorker //Класс, который нужен для удобства работы с JSON файлами
{
    public static byte[] readFile(string fileName) //Функция для чтения данных из файла в байтовый массив
    {
        string path = AppContext.BaseDirectory; //Начинаем формировать абсолютный путь
        path = Path.Combine(path, "Database files");
        Directory.CreateDirectory(path); //Гарантируем, что в случае, если файла нет (например, первый запуск), он сформируется
        path = Path.Combine(path, fileName);
        byte[] bytes = File.ReadAllBytes(path);
        return bytes;
    }

    public static void writeFile(byte[] bytes, string fileName) //Функция для записи данных в файл как байтовый массив
    {
        string path = AppContext.BaseDirectory;
        path = Path.Combine(path, "Database files", fileName);
        File.WriteAllBytes(path, bytes);
    }
}