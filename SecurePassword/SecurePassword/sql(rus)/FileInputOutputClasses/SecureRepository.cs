using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Security.Cryptography;

namespace SecurePassword;

public class SecureRepository<T> where T : IHasID //Работа с файлами с шифрованием
{
    private readonly string _fileName;
    private readonly keyManager _keyManager; //Файлик с данными для шифрования
    private List<T> _items;

    public SecureRepository(string filename, keyManager keymanager)
    {
        _fileName = filename;
        _keyManager = keymanager;
        _items = new List<T>();
        Load();
    }

    private void Load() //Функция загрузки файла с данными
    {
        byte[] fileBytes; //Массив с байтами - сериализованные данные
        try
        {
            fileBytes = FileWorker.readFile(_fileName); //Читаем сериализованные шифрованные данные
        }
        catch (FileNotFoundException)
        {
            _items = new List<T>(); //Не нашли файл - опустошили массив данных и вышли из загрузки
            return;
        }
        byte[] dek = _keyManager.GetDEK(); //Считали ДЕК из кеша
        try
        {
            byte[] plaintext = EncryptionFunctions.DecryptData(dek, fileBytes); //Расшифровали сериализованные данные
            _items = JsonSerializer.Deserialize<List<T>>(plaintext) ?? new List<T>(); //Десериализовали данные
        }
        catch (CryptographicException) //Ошибки криптографического характера
        {
            throw new InvalidOperationException($"Data decryption error. Either file {_fileName} is corrupted or using wrong data encryption key.");
        }
        catch (JsonException) //Ошибки с десереализацией
        {
            throw new InvalidOperationException($"Deserialization error in file {_fileName}!");
        }
    }

    public void Save() //Сохранение файла
    {
        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(_items); //Сериализуем данные в байты
        byte[] dek = _keyManager.GetDEK(); //Читаем ДЕК
        byte[] encryptedData = EncryptionFunctions.EncryptData(dek, plaintext); //Шифруем ДЕКом сериализованные данные
        FileWorker.writeFile(encryptedData, _fileName); //Записываем шифрованные сериализованные данные в файл
    }

    public T GetItemById(int id) => _items.FirstOrDefault(x => x.Id == id); //Получить элемент по айди (Логику могу поменять если не нравится)

    public IEnumerable<T> getAll() => _items; //Получить массив данных

    public void Add(T newItem) //Добавить по айди (если айди не занят)
    {
        if (_items.Any(x => x.Id == newItem.Id)) throw new InvalidOperationException($"Element with ID = {newItem.Id} exists already!"); //Выброс исключений если есть элемент с таким ID
        _items.Add(newItem); //Используем метод Add для List
    }

    public bool Remove(int id) //Удалить по айди
    {
        var item = GetItemById(id); //Берём элемент по айди
        if (item != null) //Если нашёлся такой элемент
        {
            _items.Remove(item); //Удаляем
            return true; //Показываем вызывающему коду, что нашли такой элемент и удалили
        }
        return false; //Иначе показываем вызывающему коду, что не нашли такой
    }

    public void Update(T newItem) //Функция для замены существующего объекта с конкретным ID
    {
        var oldItem = GetItemById(newItem.Id);
        if (oldItem == null) throw new KeyNotFoundException($"Element with ID = {newItem.Id} not found!");
        _items.Remove(oldItem); //Методы List<T>
        _items.Add(newItem);
    }
}