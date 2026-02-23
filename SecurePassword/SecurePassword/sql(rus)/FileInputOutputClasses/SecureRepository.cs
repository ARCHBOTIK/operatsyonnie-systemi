using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Security.Cryptography;

namespace SecurePassword;

public class SecureRepository<T> where T : IHasID //Незаконченнйы класс (не начатый толком)
{
    private readonly string _fileName;
    private List<T> _items;

    public SecureRepository(string filename, string password)
    {
        _fileName = filename;
        _items = new List<T>();
    }
}