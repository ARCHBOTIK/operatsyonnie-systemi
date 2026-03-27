using System;
using System.Collections.Generic;
using System.Text;

namespace SecurePassword
{
    public class MasterPasswordService
    {
        private readonly keyManager _keyManager;
        private readonly string _keyFilePath;

        public MasterPasswordService(keyManager keyManager)
        {
            _keyManager = keyManager;
            _keyFilePath = Path.Combine(FileSystem.AppDataDirectory, "keys.dat");
        }

        public bool KeyFileExists()
        {
            return File.Exists(_keyFilePath);
        }

        public void CreateMasterPassword(string password)
        {
            if (KeyFileExists())
                throw new InvalidOperationException("Файл ключей уже существует.");

            _keyManager.CreateKeyFile(password);
        }

        public void Login(string password)
        {
            _keyManager.LoadKeyFile(password);

            var dek = _keyManager.GetDEK();
            if (dek == null || dek.Length == 0)
                throw new InvalidOperationException("Не удалось загрузить ключ шифрования.");
        }

        public void ChangeMasterPassword(string oldPassword, string newPassword)
        {
            _keyManager.replaceMasterPassword(oldPassword, newPassword);
        }
    }

}
