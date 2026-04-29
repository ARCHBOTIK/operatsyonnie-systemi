using System;
using System.Collections.Generic;
using System.Text;
using System.Reflection;

namespace SecurePassword.SQL_Rus_.EncryptionClasses
{
    internal class PasswordValidator
    {
        private const double Sens_K = 0.5; //коэффициент чувствительности, я не знаю какой именно лучше ставить
        private static HashSet<string> _badPasswords;
        private static readonly object _lock = new object(); //для синхронизации

        public static int CalculateCryptographicStrength(string password) //вычисление криптографической стойкости пароля
        {
            if (password == null) throw new ArgumentNullException(nameof(password));
            int L = password.Length; //длина
            if (L == 0) return 0;
            int N = password.Distinct().Count(); //количество УНИКАЛЬНЫХ символов
            double Log2N = Math.Log(N, 2.0);
            double exponent = -Sens_K * L * Log2N; //вычисляем степень экспоненты
            double S = 1.0 - Math.Exp(exponent); //финальный штрих формулы
            double percent = S * 100.0; //перевод в проценты
            if (percent <= 5) return 0;
            if (percent >= 100) return 4;
            int level = (int)percent / 25 + 1;
            return level > 4 ? 4 : level;
        }

        public static void LoadBadPasswords() //загрузка файла с плохими паролями
        {
            var assembly = Assembly.GetExecutingAssembly(); //получение текущей сборки
            const string filepath = "SecurePassword.SQL_Rus_.Resources.badwords.txt"; //имя файла с плохими паролями
            using var stream = assembly.GetManifestResourceStream(filepath); //берем сам файл с паролями
            if (stream == null) throw new FileNotFoundException($"Resource {filepath} not found, check filepath and Build Actions."); //это чтобы при сборке было понятно, что все упало именно тут
            using var reader = new StreamReader(stream); //читатель для потока, в который мы считали файл
            string content = reader.ReadToEnd(); //читаем всё в одну строку
            var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries); //делим по знакам деления

            var passwords = new HashSet<string>(); //формируем хэшсет
            foreach (var line in lines) //заполняем хэшсет проходя по строкам
            {
                var password = line.Trim(); //удаление лидирующих пустых знаков
                if (password.Length > 0) passwords.Add(password); //проверка на существенность
            }
            _badPasswords = passwords; //пишем получившися хэшсет в кеш
        }

        private static void EnsureBadPasswordsLoaded() //удостовериться в загрузке файла - либо ничегоь не делаем, либо грузим
        {
            if (_badPasswords != null) return;
            lock (_lock)
            {
                if (_badPasswords != null) return;
                LoadBadPasswords();
            }
        }

        public static bool isPasswordCommon(string password) //проверка на базу слитых паролей
        {
            if (password == null) throw new ArgumentNullException(nameof(password));
            EnsureBadPasswordsLoaded();
            return _badPasswords.Contains(password); //ну просто проверка, есть он там или нет
        }

        public static void ClearBadPasswords() //функция для очистки хэшсета из кэша
        {
            lock (_lock)
            {
                _badPasswords?.Clear();
                _badPasswords = null;
            }
        }
    }
}
