using System;
using System.Collections.Generic;
using System.Text;

namespace XiaoZhi.Net.Server.Store
{
    public interface IStore : IDisposable
    {
        bool Add<T>(string key, T value);
        bool Contains(string key);
        T Get<T>(string key);
        int GetAllCount();
        IDictionary<string, T> GetAll<T>();
        IEnumerable<T> Get<T>(Predicate<T> criteria);
        int Remove(string key);
        int Remove(params string[] keys);
        bool Update<T>(string key, T value);
        void Clear();
    }
}
