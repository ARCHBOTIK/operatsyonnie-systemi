using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
namespace SecurePassword;

public class SimpleJsonRepository<T> where T : IHasID //Класс для обычной работы с файлами JSON, без шифрования. Для тестов, а лучше вообще не трогать
{
    private readonly string _filename;
    private List<T> _items;

    public SimpleJsonRepository(string fileName)
    {
        _filename = fileName;
        _items = new List<T>();
        Load();
    }

    private void Load() //Функция для загрузки данных из файла
    {
        try
        {
            byte[] bytes = FileWorker.readFile(_filename); //Через вспомогательный класс читаем байтовый массив
            _items = JsonSerializer.Deserialize<List<T>>(bytes) ?? new List<T>(); //Десереализуем байтовый массив, либо создаем новый пустой, если там null
        }
        catch (FileNotFoundException) //Если файла нет, создаём пустой массив (модно изменить действия, если надо)
        {
            _items = new List<T>();
        }
        catch (JsonException ex) //Отлов исключений JSON
        {
            throw new InvalidOperationException($"File {_filename} is corrupted (wrong JSON)!", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InvalidOperationException($"No access to file {_filename}!", ex);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException($"Input-output exception with file {_filename}!", ex);
        }
    }

    public void Save() //Вызывать каждый раз вручную, но можно добавить автосозранение в остальные операции, если нужно будет - обновлю
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(_items); //Сериализуем данные
        FileWorker.writeFile(bytes, _filename); //С помощью вспомогательного класса записываем в файл
    }

    public T GetItemById(int id)
    {
        return _items.FirstOrDefault(x => x.Id == id); //Получаем объект по айди, могу поменять реализацию, просто так выглядит круто типа предикаты всё такое
    }

    public IEnumerable<T> GetAll() => _items; //Взять все элементы массива

    public void Add(T newItem)
    {
        if (_items.Any(x => x.Id == newItem.Id)) throw new InvalidOperationException($"Element with ID = {newItem.Id} exists already!"); //Выброс исключений если есть элемент с таким ID
        _items.Add(newItem); //Используем метод Add для List
    }

    public bool Remove(int id)
    {
        var item = GetItemById(id); //Берём элемент по айди
        if (item != null) //Если нашёлся такой элемент
        {
            _items.Remove(item); //Удаляем
            return true; //Показываем вызывающему коду, что нашли такой элемент и удалили
        }
        return false; //Иначе показываем вызывающему коду, что не нашли такой
    }

    public void Update(T newItem) //Функция для замены объекта с конкретным ID
    {
        var oldItem = GetItemById(newItem.Id);
        if (oldItem == null) throw new KeyNotFoundException($"Element with ID = {newItem.Id} not found!");
        _items.Remove(oldItem);
        _items.Add(newItem);
    }
}
