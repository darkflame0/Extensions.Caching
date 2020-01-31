using System;
using Newtonsoft.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;

namespace Microsoft.Extensions.Caching.Distributed
{
    public static class DistributedCacheExtensions
    {
        static readonly JsonSerializerSettings _settings = new JsonSerializerSettings() { MissingMemberHandling = MissingMemberHandling.Error };

        public static TItem Get<TItem>(this IDistributedCache cache, string key)
        {
            var str = cache.GetString(key);
            if (str == null)
            {
                return default!;
            }
            try
            {
                if(typeof(TItem).IsTuple())
                {
                    return JsonConvert.DeserializeObject<TItem>(str, _settings);
                }
                return JsonConvert.DeserializeObject<TItem>(str);

            }
            catch (JsonSerializationException)
            {
                return default!;
            }
        }
        public static async Task<TItem> GetAsync<TItem>(this IDistributedCache cache, string key, CancellationToken token = default)
        {
            var str = await cache.GetStringAsync(key, token).ConfigureAwait(false);
            if (str == null)
            {
                return default!;
            }
            try
            {
                if (typeof(TItem).IsTuple())
                {
                    return JsonConvert.DeserializeObject<TItem>(str, _settings);
                }
                return JsonConvert.DeserializeObject<TItem>(str);
            }
            catch (JsonSerializationException)
            {
                return default!;
            }
        }
        public static TItem GetOrCreate<TItem>(this IDistributedCache cache, string key, Func<DistributedCacheEntryOptions, TItem> factory)
        {
            TItem Create()
            {
                var item = factory(new DistributedCacheEntryOptions());
                cache.Set(key, item);
                return item;
            }
            var str = cache.GetString(key);
            if (str == null)
            {
                return Create();
            }
            try
            {
                if (typeof(TItem).IsTuple())
                {
                    return JsonConvert.DeserializeObject<TItem>(str, _settings);
                }
                return JsonConvert.DeserializeObject<TItem>(str);
            }
            catch (JsonSerializationException)
            {
                return Create();
            }
        }
        public static async Task<TItem> GetOrCreateAsync<TItem>(this IDistributedCache cache, string key, Func<DistributedCacheEntryOptions, Task<TItem>> factory, CancellationToken token = default)
        {
            async Task<TItem> CreateAsync()
            {
                var op = new DistributedCacheEntryOptions();
                var item = await factory(op);
                _ = cache.SetAsync(key, item, op, token);
                return item;
            }
            var str = await cache.GetStringAsync(key).ConfigureAwait(false);
            if (str == null)
            {
                return await CreateAsync().ConfigureAwait(false);
            }
            try
            {
                if (typeof(TItem).IsTuple())
                {
                    return JsonConvert.DeserializeObject<TItem>(str, _settings);
                }
                return JsonConvert.DeserializeObject<TItem>(str);
            }
            catch (JsonSerializationException)
            {
                return await CreateAsync();
            }
        }
        public static void Set<TItem>(this IDistributedCache cache, string key, TItem value)
        {
            cache.Set(key, value, new DistributedCacheEntryOptions());
        }
        public static void Set<TItem>(this IDistributedCache cache, string key, TItem value, DateTimeOffset absoluteExpiration)
        {
            cache.Set(key, value, new DistributedCacheEntryOptions() { AbsoluteExpiration = absoluteExpiration });
        }
        public static void Set<TItem>(this IDistributedCache cache, string key, TItem value, TimeSpan absoluteExpirationRelativeToNow)
        {
            cache.Set(key, value, new DistributedCacheEntryOptions() { AbsoluteExpirationRelativeToNow = absoluteExpirationRelativeToNow });
        }
        public static void Set<TItem>(this IDistributedCache cache, string key, TItem value, DistributedCacheEntryOptions options)
        {
            cache.SetString(key, JsonConvert.SerializeObject(value), options);
        }
        public static Task SetAsync<TItem>(this IDistributedCache cache, string key, TItem value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            return cache.SetStringAsync(key, JsonConvert.SerializeObject(value), options, token);
        }
        internal static bool IsTuple(this Type tuple)
        {
            if (!tuple.IsGenericType)
                return false;
            return typeof(ITuple).IsAssignableFrom(tuple);
        }
    }
}