namespace SecurePassword;

internal class FileWorker
{
    public static byte[] readFile(string fileName)
    {
        string path = FileSystem.AppDataDirectory;
        Directory.CreateDirectory(path);
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