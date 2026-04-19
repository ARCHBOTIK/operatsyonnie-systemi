using System;
using System.Collections.Generic;
using System.Linq;

namespace SecurePassword
{
    // Класс для передачи данных из формы в SecureRepository
    public class FormDataProcessor
    {
        private keyManager _keyManager;
        private bool _isInitialized;

        // Словарь для хранения репозиториев разных типов
        private Dictionary<Type, object> _repositories;

        public FormDataProcessor()
        {
            _repositories = new Dictionary<Type, object>();
            _isInitialized = false;
        }

        // Инициализация с мастер-паролем
        public bool Initialize(string password, bool createNew = false)
        {
            try
            {
                _keyManager = new keyManager("master.key");

                if (createNew)
                {
                    _keyManager.CreateKeyFile(password);
                }
                else
                {
                    _keyManager.LoadKeyFile(password);
                }

                _isInitialized = true;
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка инициализации: {ex.Message}");
                return false;
            }
        }

        // Получить или создать репозиторий для конкретного типа
        private SecureRepository<T> GetRepository<T>(string filename) where T : class, IHasID, new()
        {
            if (!_isInitialized)
                throw new InvalidOperationException("Сначала вызовите Initialize()");

            Type type = typeof(T);
            if (!_repositories.ContainsKey(type))
            {
                var repository = new SecureRepository<T>(filename, _keyManager);
                _repositories[type] = repository;
            }

            return (SecureRepository<T>)_repositories[type];
        }

        // Добавить новую запись из формы
        // Использует SecureRepository.Add()
        public bool AddRecord<T>(T record, string filename) where T : class, IHasID, new()
        {
            try
            {
                var repository = GetRepository<T>(filename);
                repository.Add(record);
                repository.Save(); // Сохраняем сразу после добавления
                return true;
            }
            catch (InvalidOperationException ex) // Элемент с таким ID уже существует
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при добавлении: {ex.Message}");
                return false;
            }
        }

        // Обновить существующую запись из формы
        // Использует SecureRepository.Update()
        public bool UpdateRecord<T>(T record, string filename) where T : class, IHasID, new()
        {
            try
            {
                var repository = GetRepository<T>(filename);
                repository.Update(record);
                repository.Save(); // Сохраняем после обновления
                return true;
            }
            catch (KeyNotFoundException ex) // Элемент не найден
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при обновлении: {ex.Message}");
                return false;
            }
        }

        // Удалить запись по ID
        // Использует SecureRepository.Remove() и SecureRepository.Save()
        public bool DeleteRecord<T>(int id, string filename) where T : class, IHasID, new()
        {
            try
            {
                var repository = GetRepository<T>(filename);
                bool deleted = repository.Remove(id);

                if (deleted)
                {
                    repository.Save(); // Сохраняем после удаления
                }

                return deleted;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при удалении: {ex.Message}");
                return false;
            }
        }

        // Получить запись по ID для отображения в форме
        // Использует SecureRepository.GetItemById()
        public T GetRecordById<T>(int id, string filename) where T : class, IHasID, new()
        {
            try
            {
                var repository = GetRepository<T>(filename);
                return repository.GetItemById(id);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при получении записи: {ex.Message}");
                return null;
            }
        }

        // Получить все записи для отображения в списке/таблице
        // Использует SecureRepository.getAll()
        public List<T> GetAllRecords<T>(string filename) where T : class, IHasID, new()
        {
            try
            {
                var repository = GetRepository<T>(filename);
                return repository.getAll().ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при получении списка: {ex.Message}");
                return new List<T>();
            }
        }

        // Проверить существование записи с указанным ID
        // Использует SecureRepository.GetItemById()
        public bool RecordExists<T>(int id, string filename) where T : class, IHasID, new()
        {
            try
            {
                var repository = GetRepository<T>(filename);
                return repository.GetItemById(id) != null;
            }
            catch
            {
                return false;
            }
        }

        // Сортировка с ключом
        public List<T> SortRecord<T>(
            string filename,
            Func<T, object> keySelector,
            bool ascending = true
        ) where T : class, IHasID, new()
        {
            try
            {
                var records = GetAllRecords<T>(filename);

                return ascending
                    ? records.OrderBy(keySelector).ToList()
                    : records.OrderByDescending(keySelector).ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при сортировке: {ex.Message}");
                return new List<T>();
            }
        }

        // Сохранить все изменения
        public void SaveAllChanges()
        {
            foreach (var repo in _repositories.Values)
            {
                var saveMethod = repo.GetType().GetMethod("Save");
                saveMethod?.Invoke(repo, null);
            }
        }
    }
}
