using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace XiaoZhi.Net.Server.Store
{
    internal class DefaultMemoryStore : IStore, IDisposable
    {
        private static readonly int ShardCount = Environment.ProcessorCount * 2;
        ConcurrentDictionary<string, object>[] _shards = new ConcurrentDictionary<string, object>[ShardCount];

        public static DefaultMemoryStore Default => new DefaultMemoryStore();

        public DefaultMemoryStore()
        {
            // 初始化分片
            for (int i = 0; i < ShardCount; i++)
            {
                _shards[i] = new ConcurrentDictionary<string, object>();
            }
        }

        public bool Add<T>(string key, T value)
        {
            if (string.IsNullOrEmpty(key) || value == null)
            {
                throw new ArgumentNullException(nameof(key));
            }
            return GetShardCache(key).TryAdd(key, value);
        }

        public bool Contains(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentNullException(nameof(key));
            }
            return GetShardCache(key).ContainsKey(key);
        }

        public T Get<T>(string key)
        {
            try
            {
                if (string.IsNullOrEmpty(key))
                {
                    throw new ArgumentNullException(nameof(key));
                }
                if (GetShardCache(key).TryGetValue(key, out var value))
                {
                    return (T)value;
                }
                return default;
            }
            catch
            {
                //log
                return default;
            }
        }

        public int Remove(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentNullException(nameof(key));
            }

            var shard = GetShardCache(key);
            return shard.TryRemove(key, out _) ? 1 : 0;
        }

        public int Remove(params string[] keys)
        {
            if (keys == null)
            {
                throw new ArgumentNullException(nameof(keys));
            }
            int removedCount = 0;
            var shardGroups = keys.Where(k => !string.IsNullOrEmpty(k))
                                  .GroupBy(GetShardIndex);

            foreach (var group in shardGroups)
            {
                var shard = _shards[group.Key];
                foreach (var key in group)
                {
                    if (shard.TryRemove(key, out _))
                        removedCount++;
                }
            }
            return removedCount;
        }

        public bool Update<T>(string key, T value)
        {
            if (string.IsNullOrEmpty(key) || value == null)
            {
                throw new ArgumentNullException(nameof(key));
            }
            var shard = GetShardCache(key);
            return shard.TryGetValue(key, out var oldEntry) &&
                   shard.TryUpdate(key, value, oldEntry);
        }

        public IDictionary<string, T> GetAll<T>()
        {
            var result = new ConcurrentDictionary<string, T>();
            foreach (var shard in _shards)
            {
                foreach (var kvp in shard)
                {
                    if (kvp.Value is T value)
                    {
                        result.TryAdd(kvp.Key, value);
                    }
                }
            }
            return result;
        }

        public IDictionary<string, T> Get<T>(ICollection<string> keys)
        {
            if (keys == null || keys.Count == 0)
            {
                throw new ArgumentNullException(nameof(keys));
            }
            var result = new ConcurrentDictionary<string, T>();
            var validKeys = keys.Where(k => !string.IsNullOrEmpty(k)).ToList();
            var shardGroups = validKeys.GroupBy(GetShardIndex);

            foreach (var group in shardGroups)
            {
                var shard = _shards[group.Key];
                foreach (var key in group)
                {
                    if (shard.TryGetValue(key, out var entry))
                    {
                        result.TryAdd(key, (T)entry);
                    }
                }
            }
            return result;
        }

        public void Clear()
        {
            foreach (var shard in _shards)
            {
                shard.Clear();
            }
        }

        private ConcurrentDictionary<string, object> GetShardCache(string key)
        {
            return _shards[GetShardIndex(key)];
        }
        private static int GetShardIndex(string key)
        {
            const uint fnvPrime = 16777619;
            uint hash = 2166136261;

            var bytes = Encoding.UTF8.GetBytes(key);
            foreach (byte b in bytes)
            {
                hash ^= b;
                hash *= fnvPrime;
            }
            return (int)(hash % ShardCount);
        }
        #region IDisposable
        public void Dispose()
        {
            Clear();
        }
        #endregion
    }
}
