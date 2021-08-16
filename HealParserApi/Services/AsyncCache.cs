using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HealParserApi.Services
{
    public class AsyncCache<T>
    {
        private readonly ConcurrentDictionary<string, CacheItem<T>> cache;
        private TimeSpan ItemExpires { get; set; }

        public AsyncCache(TimeSpan expiration) {

            this.cache = new ConcurrentDictionary<string, CacheItem<T>>();
            this.ItemExpires = expiration;
        }

        public async Task Set(string key, T data)
        {
            if (this.cache.ContainsKey(key))
            {
                this.cache.TryRemove(key, out _);
            }

            var cItem = new CacheItem<T> { Data = data, Expiration = DateTime.UtcNow.Add(this.ItemExpires) };
            this.cache.TryAdd(key, cItem);
        }

        public async Task<T> Get(string key)
        {
            if (!this.cache.ContainsKey(key))
            {
                return default;
            }

            var item = this.cache[key];
            if (item.Expiration < DateTime.UtcNow)
            {
                await this.Expire(key);
                return default;
            }

            return item.Data;
        }

        public async Task Expire(string key)
        {
            this.cache.TryRemove(key, out _);
        }
    }

    public class CacheItem<T>
    {
        public DateTime Expiration { get; set; }

        public T Data { get; set; }
    }
}
