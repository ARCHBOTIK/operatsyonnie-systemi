using System;
using System.Collections.Generic;
using System.Linq;

namespace SecurePassword
{
    public class FormDataProcessor
    {
        private keyManager _keyManager;
        private bool _isInitialized;

        private Dictionary<Type, object> _repositories;

        public FormDataProcessor()
        {
            _repositories = new Dictionary<Type, object>();
            _isInitialized = false;
        }

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
                Console.WriteLine($"РћС€РёР±РєР° РёРЅРёС†РёР°Р»РёР·Р°С†РёРё: {ex.Message}");
                return false;
            }
        }

        private SecureRepository<T> GetRepository<T>(string filename) where T : class, IHasID, new()
        {
            if (!_isInitialized)
                throw new InvalidOperationException("РЎРЅР°С‡Р°Р»Р° РІС‹Р·РѕРІРёС‚Рµ Initialize()");

            Type type = typeof(T);
            if (!_repositories.ContainsKey(type))
            {
                var repository = new SecureRepository<T>(filename, _keyManager);
                _repositories[type] = repository;
            }

            return (SecureRepository<T>)_repositories[type];
        }

        public bool AddRecord<T>(T record, string filename) where T : class, IHasID, new()
        {
            try
            {
                var repository = GetRepository<T>(filename);
                repository.Add(record);
                repository.Save(); // РЎРѕС…СЂР°РЅСЏРµРј СЃСЂР°Р·Сѓ РїРѕСЃР»Рµ РґРѕР±Р°РІР»РµРЅРёСЏ
                return true;
            }
            catch (InvalidOperationException ex) // Р­Р»РµРјРµРЅС‚ СЃ С‚Р°РєРёРј ID СѓР¶Рµ СЃСѓС‰РµСЃС‚РІСѓРµС‚
            {
                Console.WriteLine($"РћС€РёР±РєР°: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"РћС€РёР±РєР° РїСЂРё РґРѕР±Р°РІР»РµРЅРёРё: {ex.Message}");
                return false;
            }
        }

        public bool UpdateRecord<T>(T record, string filename) where T : class, IHasID, new()
        {
            try
            {
                var repository = GetRepository<T>(filename);
                repository.Update(record);
                repository.Save(); // РЎРѕС…СЂР°РЅСЏРµРј РїРѕСЃР»Рµ РѕР±РЅРѕРІР»РµРЅРёСЏ
                return true;
            }
            catch (KeyNotFoundException ex) // Р­Р»РµРјРµРЅС‚ РЅРµ РЅР°Р№РґРµРЅ
            {
                Console.WriteLine($"РћС€РёР±РєР°: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"РћС€РёР±РєР° РїСЂРё РѕР±РЅРѕРІР»РµРЅРёРё: {ex.Message}");
                return false;
            }
        }

        public bool DeleteRecord<T>(int id, string filename) where T : class, IHasID, new()
        {
            try
            {
                var repository = GetRepository<T>(filename);
                bool deleted = repository.Remove(id);

                if (deleted)
                {
                    repository.Save(); // РЎРѕС…СЂР°РЅСЏРµРј РїРѕСЃР»Рµ СѓРґР°Р»РµРЅРёСЏ
                }

                return deleted;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"РћС€РёР±РєР° РїСЂРё СѓРґР°Р»РµРЅРёРё: {ex.Message}");
                return false;
            }
        }

        public T GetRecordById<T>(int id, string filename) where T : class, IHasID, new()
        {
            try
            {
                var repository = GetRepository<T>(filename);
                return repository.GetItemById(id);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"РћС€РёР±РєР° РїСЂРё РїРѕР»СѓС‡РµРЅРёРё Р·Р°РїРёСЃРё: {ex.Message}");
                return null;
            }
        }

        public List<T> GetAllRecords<T>(string filename) where T : class, IHasID, new()
        {
            try
            {
                var repository = GetRepository<T>(filename);
                return repository.getAll().ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"РћС€РёР±РєР° РїСЂРё РїРѕР»СѓС‡РµРЅРёРё СЃРїРёСЃРєР°: {ex.Message}");
                return new List<T>();
            }
        }

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
                Console.WriteLine($"РћС€РёР±РєР° РїСЂРё СЃРѕСЂС‚РёСЂРѕРІРєРµ: {ex.Message}");
                return new List<T>();
            }
        }

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
