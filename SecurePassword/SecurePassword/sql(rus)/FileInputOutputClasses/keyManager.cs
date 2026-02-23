using System;
using System.Linq;
using SecurePassword;
namespace SecurePassword;

public class keyManager //Класс для работы с файлом, где хранятся как раз те самые соль, кек, дек
{
    private readonly string _keyFilePath;
    private byte[] _salt;
    private byte[] _encryptedDek;


    public keyManager(string keyFilePath)
    {
        _keyFilePath = keyFilePath;
    }

    public void CreateKeyFile(string password) //Создать файл
    {
        byte[] dek = EncryptionFunctions.GenerateDEK(32); //Генерируем ДЕК
        _salt = EncryptionFunctions.GenerateSalt(16); //Генерируем СОЛЬ
        byte[] kek = EncryptionFunctions.GenerateKEKwArgon2id(password, _salt, 0); //Получаем из пароля КЕК
        byte[] nonce = new byte[12];
        byte[] tag = new byte[16];
        _encryptedDek = EncryptionFunctions.EncryptDEKwithGCM(dek, kek, out nonce, out tag); //Шифруем ДЕК
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
        byte[] kek = EncryptionFunctions.GenerateKEKwArgon2id(password, _salt, 0); //Восстанавливаем КЕК
        byte[] dek = EncryptionFunctions.DecryptDEK(kek, _encryptedDek); //А это расшифровка ДЕКа, если будет можно его кешировать. Нет - удалю эти две строки и подумаю ещё
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
}