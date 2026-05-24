using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
namespace SecurePassword;

public class SimpleJsonRepository<T> where T : IHasID
{
    private readonly string _filename;
    private List<T> _items;

    public SimpleJsonRepository(string fileName)
    {
        _filename = fileName;
        _items = new List<T>();
        Load();
    }

    private void Load()
    {
        try
        {
            byte[] bytes = FileWorker.readFile(_filename);
            _items = JsonSerializer.Deserialize<List<T>>(bytes) ?? new List<T>();
        }
        catch (FileNotFoundException)
        {
            _items = new List<T>();
        }
        catch (JsonException ex)
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

    public void Save()
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(_items);
        FileWorker.writeFile(bytes, _filename);
    }

    public T GetItemById(int id)
    {
        return _items.FirstOrDefault(x => x.Id == id);
    }

    public IEnumerable<T> GetAll() => _items;

    public void Add(T newItem)
    {
        if (_items.Any(x => x.Id == newItem.Id)) throw new InvalidOperationException($"Element with ID = {newItem.Id} exists already!");
        _items.Add(newItem);
    }

    public bool Remove(int id)
    {
        var item = GetItemById(id);
        if (item != null)
        {
            _items.Remove(item);
            return true;
        }
        return false;
    }

    public void Update(T newItem)
    {
        var oldItem = GetItemById(newItem.Id);
        if (oldItem == null) throw new KeyNotFoundException($"Element with ID = {newItem.Id} not found!");
        _items.Remove(oldItem);
        _items.Add(newItem);
    }
}
