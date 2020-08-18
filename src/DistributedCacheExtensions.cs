using System;
using Newtonsoft.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using System.Collections.Concurrent;

namespace Microsoft.Extensions.Caching.Distributed
{
    public static class DistributedCacheExtensions
    {
        static readonly JsonSerializerSettings _settings = new JsonSerializerSettings() { MissingMemberHandling = MissingMemberHandling.Error };

        private static readonly ConcurrentDictionary<int, SemaphoreSlim> _semaphores = new ConcurrentDictionary<int, SemaphoreSlim>();

        public static async Task<T> GetOrCreateAsync<T>(this IDistributedCache cache, string key, Func<DistributedCacheEntryOptions, Task<T>> factory, Func<T, bool>? notExpiredCheck = null)
        {
            static bool DefaultCheck(T data) => true;
            notExpiredCheck ??= DefaultCheck;
            var value = await cache.GetAsync<T>(key);
            if (value != null && notExpiredCheck(value))
                return value;

            var isOwner = false;
            var semaphoreKey = (cache, key).GetHashCode();
            if (!_semaphores.TryGetValue(semaphoreKey, out var semaphore))
            {
                SemaphoreSlim? createdSemaphore = null;
                semaphore = _semaphores.GetOrAdd(semaphoreKey, k => createdSemaphore = new SemaphoreSlim(1)); // Try to add the value, this is not atomic, so multiple semaphores could be created, but just one will be stored!

                if (createdSemaphore != semaphore)
                    createdSemaphore?.Dispose(); // This semaphore was not the one that made it into the dictionary, will not be used!
                else
                    isOwner = true;
            }

            await semaphore.WaitAsync()
                           .ConfigureAwait(false); // Await the semaphore!
            try
            {
                value = await cache.GetAsync<T>(key);
                if (value == null || !notExpiredCheck(value))
                {
                    var op = new DistributedCacheEntryOptions();
                    await cache.SetAsync(key, await factory(op), op);
                }

                return value;
            }
            finally
            {
                if (isOwner)
                    _semaphores.TryRemove(semaphoreKey, out _);
                semaphore.Release();
            }
        }
        public static TItem Get<TItem>(this IDistributedCache cache, string key)
        {
            var str = cache.GetString(key);
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
        public static Task SetAsync<TItem>(this IDistributedCache cache, string key, TItem value, CancellationToken token = default)
        {
            return cache.SetAsync(key, value, new DistributedCacheEntryOptions(), token);
        }
        public static Task SetAsync<TItem>(this IDistributedCache cache, string key, TItem value, DateTimeOffset absoluteExpiration, CancellationToken token = default)
        {
            return cache.SetAsync(key, value, new DistributedCacheEntryOptions() { AbsoluteExpiration = absoluteExpiration }, token);
        }
        public static Task SetAsync<TItem>(this IDistributedCache cache, string key, TItem value, TimeSpan absoluteExpirationRelativeToNow, CancellationToken token = default)
        {
            return cache.SetAsync(key, value, new DistributedCacheEntryOptions() { AbsoluteExpirationRelativeToNow = absoluteExpirationRelativeToNow }, token);
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