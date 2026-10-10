using DBreeze;
using System.Text.Json;
namespace Realm.AdminServer.Services;

public class DataStoreService : IDisposable
{
    private readonly DBreezeEngine _engine;
    private readonly string _dataDirectory;

    public string DataDirectory => _dataDirectory;

    public DataStoreService(string dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            throw new ArgumentException("Data directory cannot be null or empty.", nameof(dataDirectory));
        }

        _dataDirectory = Path.GetFullPath(dataDirectory);
        Directory.CreateDirectory(_dataDirectory);
        _engine = new DBreezeEngine(_dataDirectory);
    }

    public T? Get<T>(string collection, string id)
    {
        using var t = _engine.GetTransaction();
        var row = t.Select<string, string>(collection, id);
        if (row.Exists)
        {
            return JsonSerializer.Deserialize<T>(row.Value);
        }
        return default;
    }

    public IEnumerable<T> GetAll<T>(string collection)
    {
        using var t = _engine.GetTransaction();
        var list = new List<T>();
        foreach (var row in t.SelectForward<string, string>(collection))
        {
            var item = JsonSerializer.Deserialize<T>(row.Value);
            if (item != null)
            {
                list.Add(item);
            }
        }
        return list;
    }

    public Dictionary<string, T> GetAllWithKeys<T>(string collection)
    {
        using var t = _engine.GetTransaction();
        var dict = new Dictionary<string, T>();
        foreach (var row in t.SelectForward<string, string>(collection))
        {
            var item = JsonSerializer.Deserialize<T>(row.Value);
            if (item != null)
            {
                dict[row.Key] = item;
            }
        }
        return dict;
    }


    public void Upsert<T>(string collection, string id, T data)
    {
        using var t = _engine.GetTransaction();
        t.SynchronizeTables(collection);
        var json = JsonSerializer.Serialize(data);
        t.Insert(collection, id, json);
        t.Commit();
    }

    public void Delete(string collection, string id)
    {
        using var t = _engine.GetTransaction();
        t.SynchronizeTables(collection);
        t.RemoveKey(collection, id);
        t.Commit();
    }

    public void DeleteMany(string collection, IEnumerable<string> ids)
    {
        using var t = _engine.GetTransaction();
        t.SynchronizeTables(collection);
        foreach (var id in ids)
        {
            t.RemoveKey(collection, id);
        }
        t.Commit();
    }

    public void Dispose()
    {
        _engine.Dispose();
        GC.SuppressFinalize(this);
    }
}
