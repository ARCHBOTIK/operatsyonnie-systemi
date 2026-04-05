using System;
using System.Linq;
using SecurePassword;
using SQLite;
namespace SecurePassword;

public class keyManager //Класс для работы с файлом, где хранятся как раз те самые соль, кек, дек
{
    private readonly string _keyFilePath;
    private byte[] _salt;
    private byte[] _encryptedDek;
    private byte[] _dek;
    OSType _systemType = 0; //Я ума не приложу как узнать тип системы))

    public keyManager(string keyFilePath)
    {
        _keyFilePath = keyFilePath;
    }

    public void CreateKeyFile(string password) //Создать файл
    {
        _dek = EncryptionFunctions.GenerateDEK(32); //Генерируем ДЕК
        _salt = EncryptionFunctions.GenerateSalt(16); //Генерируем СОЛЬ
#if WINDOWS
        byte[] kek = EncryptionFunctions.GenerateKEKwArgon2id(password, _salt, 0); //Восстанавливаем КЕК
#elif ANDROID
        byte[] kek = EncryptionFunctions.GenerateKEKwArgon2id(password, _salt, OSType.Android); //Восстанавливаем КЕК
#endif
        byte[] nonce = new byte[12];
        byte[] tag = new byte[16];
        _encryptedDek = EncryptionFunctions.EncryptDEKwithGCM(_dek, kek, out nonce, out tag); //Шифруем ДЕК
        SaveKeyFile(); //Сохраняем файл
    }

    public void LoadKeyFile(string password) //Загрузить файл
    {
        byte[] keyFileBytes = FileWorker.readFile(Path.GetFileName(_keyFilePath)); //Читаем файл с помозью вспомогательного класса
        using (var ms = new MemoryStream(keyFileBytes))
        using (var br = new BinaryReader(ms)) //Это для работы с двоичными данными
        {
            _salt = br.ReadBytes(16); //Читаем соль
            _encryptedDek = br.ReadBytes(60); //Читаем зашифрованный ДЕК
        }
        var sw = System.Diagnostics.Stopwatch.StartNew();
#if WINDOWS
        byte[] kek = EncryptionFunctions.GenerateKEKwArgon2id(password, _salt, 0); //Восстанавливаем КЕК
#elif ANDROID
        byte[] kek = EncryptionFunctions.GenerateKEKwArgon2id(password, _salt, OSType.Android); //Восстанавливаем КЕК
#endif 
        sw.Stop();
        System.Diagnostics.Debug.WriteLine($"ARGON2 ONLY = {sw.ElapsedMilliseconds} ms");
        _dek = EncryptionFunctions.DecryptDEK(kek, _encryptedDek); //Расшифровка ДЕКа
    }

    private void SaveKeyFile() //Сохранить файл
    {
        using (var ms = new MemoryStream())
        using (var bw = new BinaryWriter(ms)) //Ну это для работы с потоком двоичных данных
        {
            bw.Write(_salt); //Записываем соль
            bw.Write(_encryptedDek); //Записываем зашифрованный ДЕК. Если всё-таки надо, добавлю запись параметров к аргону
            FileWorker.writeFile(ms.ToArray(), Path.GetFileName(_keyFilePath)); //Записываем это всё в файл
        }
    }

    public byte[] GetDEK() //Получаение ДЕК из кеша
    {
        if (_dek == null) throw new InvalidOperationException("DEK was not loaded. Call Load() method.");
        return _dek;
    }

    public void ChangePassword(string newPassword) //Смена пароля (если не будем добавлять функцию смены мастер-пароля, может пригодиться для ротации. Нет - удалю)
    {
        if (_dek == null) throw new InvalidOperationException("DEK was not loaded.");
        _salt = EncryptionFunctions.GenerateSalt(); //Создаем новую соль
        byte[] kek = EncryptionFunctions.GenerateKEKwArgon2id(newPassword, _salt, _systemType); //Создаем новый KEK
        byte[] nonce; byte[] tag;
        _encryptedDek = EncryptionFunctions.EncryptDEKwithGCM(_dek, kek, out nonce, out tag); //Шифруем старый DEK новым KEK
        SaveKeyFile(); //Сохраняем файл с данными
    }
    public void replaceMasterPassword(string oldPassword, string newPassword)
    {
        // Расшифровывание DEK старым мастер-паролем
        LoadKeyFile(oldPassword);

        // Перешифровывание DEK новым мастер-паролем
        ChangePassword(newPassword);
    }
}
