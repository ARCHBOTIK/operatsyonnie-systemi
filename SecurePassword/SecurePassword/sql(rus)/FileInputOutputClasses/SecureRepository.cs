using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace SecurePassword;

public class SecureRepository<T> where T : IHasID
{
    private readonly string _fileName;
    private readonly keyManager _keyManager;
    private List<T> _items;
    private bool _isLoaded;
    private int _loadedKeyVersion = -1;

    public SecureRepository(string filename, keyManager keymanager)
    {
        _fileName = filename;
        _keyManager = keymanager;
        _items = [];
    }

    private void Load()
    {
        byte[] fileBytes;

        try
        {
            fileBytes = FileWorker.readFile(_fileName);
        }
        catch (FileNotFoundException)
        {
            _items = [];
            return;
        }

        byte[] dek = _keyManager.GetDEK();

        try
        {
            byte[] plaintext = EncryptionFunctions.DecryptData(dek, fileBytes);
            _items = JsonSerializer.Deserialize<List<T>>(plaintext) ?? [];
        }
        catch (CryptographicException)
        {
            throw new InvalidOperationException($"Data decryption error. Either file {_fileName} is corrupted or using wrong data encryption key.");
        }
        catch (JsonException)
        {
            throw new InvalidOperationException($"Deserialization error in file {_fileName}!");
        }
    }

    private bool EnsureLoaded()
    {
        if (_isLoaded && _loadedKeyVersion == _keyManager.KeyVersion && _keyManager.IsDekLoaded())
            return true;

        if (!_keyManager.IsDekLoaded())
        {
            _items = [];
            _isLoaded = false;
            _loadedKeyVersion = -1;
            return false;
        }

        Load();
        _isLoaded = true;
        _loadedKeyVersion = _keyManager.KeyVersion;
        return true;
    }

    private void EnsureUnlocked()
    {
        if (!EnsureLoaded())
            throw new InvalidOperationException("Vault is locked. Call LoadKeyFile first.");
    }

    public void Save()
    {
        EnsureUnlocked();

        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(_items);
        byte[] dek = _keyManager.GetDEK();
        byte[] encryptedData = EncryptionFunctions.EncryptData(dek, plaintext);
        FileWorker.writeFile(encryptedData, _fileName);
    }

    public T GetItemById(int id)
    {
        return !EnsureLoaded() ? default : _items.FirstOrDefault(x => x.Id == id);
    }

    public IEnumerable<T> getAll()
    {
        return !EnsureLoaded() ? [] : _items;
    }

    public void Add(T newItem)
    {
        EnsureUnlocked();

        if (_items.Any(x => x.Id == newItem.Id))
            throw new InvalidOperationException($"Element with ID = {newItem.Id} exists already!");

        _items.Add(newItem);
    }

    public bool Remove(int id)
    {
        EnsureUnlocked();

        var item = _items.FirstOrDefault(x => x.Id == id);
        if (item is null)
            return false;

        _items.Remove(item);
        return true;
    }

    public void Update(T newItem)
    {
        EnsureUnlocked();

        var oldItem = _items.FirstOrDefault(x => x.Id == newItem.Id);
        if (oldItem is null)
            throw new KeyNotFoundException($"Element with ID = {newItem.Id} not found!");

        _items.Remove(oldItem);
        _items.Add(newItem);
    }

    public void DeleteDatabase()
    {
        try
        {
            if (File.Exists(_fileName))
                File.Delete(_fileName);

            _items.Clear();
            _isLoaded = true;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to delete database file {_fileName}.", ex);
        }
    }
}
