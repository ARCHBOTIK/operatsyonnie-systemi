namespace SecurePassword;

internal class FileWorker //Класс, который нужен для удобства работы с JSON файлами
{
    public static byte[] readFile(string fileName) //Функция для чтения данных из файла в байтовый массив
    {
        string path = FileSystem.AppDataDirectory; //Начинаем формировать абсолютный путь
        Directory.CreateDirectory(path); //Гарантируем, что в случае, если файла нет (например, первый запуск), он сформируется
        path = Path.Combine(path, fileName);
        byte[] bytes = File.ReadAllBytes(path);
        return bytes;
    }

    public static void writeFile(byte[] bytes, string fileName)
    {
        string baseDir = FileSystem.AppDataDirectory;
        Directory.CreateDirectory(baseDir);
        string path = Path.Combine(baseDir, fileName);
        File.WriteAllBytes(path, bytes);
    }
}