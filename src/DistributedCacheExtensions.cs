using System;
using Newtonsoft.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using StackExchange.Redis;
using System.Reflection;
using System.Linq;
using RedLockNet.SERedis;
using RedLockNet;
using RedLockNet.SERedis.Configuration;

namespace Microsoft.Extensions.Caching.Distributed
{
    public static class DistributedCacheExtensions
    {
        static readonly JsonSerializerSettings _settings = new JsonSerializerSettings() { MissingMemberHandling = MissingMemberHandling.Error, ObjectCreationHandling = ObjectCreationHandling.Replace };
        private static readonly object _locker = new object();
        private static volatile ConnectionMultiplexer? _connection;
        private static volatile IDistributedLockFactory? _redlockFactory;
        private static void InitLock(this IDistributedCache cache)
        {
            lock (_locker)
            {
                if (_redlockFactory == null)
                {
                    _connection = (ConnectionMultiplexer?)(cache.GetType().GetField(nameof(_connection), BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(cache)) ?? throw new ArgumentException("fail to get ConnectionMultiplexer");
                    _redlockFactory = RedLockFactory.Create(new RedLockMultiplexer[] { _connection });
                }
            }
        }

        public static async Task<T> GetOrCreateAsync<T>(this IDistributedCache cache, string key, Func<DistributedCacheEntryOptions, Task<T>> factory, Func<T, bool>? notExpiredCheck = null, CancellationToken cancellationToken = default)
        {
            static bool DefaultCheck(T data) => true;
            notExpiredCheck ??= DefaultCheck;
            var value = await cache.GetAsync<T>(key, token: cancellationToken);
            if (value != null && notExpiredCheck(value))
                return value;
            if (_redlockFactory == null)
            {
                cache.InitLock();
            }
            using var redLock = await _redlockFactory!.CreateLockAsync($"{key}", TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(0.5), cancellationToken: cancellationToken);
            if (!redLock.IsAcquired)
            {
                throw new Exception($"Failed to get lock: Resource:{redLock.Resource} Status: {redLock.Status}");
            }
            value = await cache.GetAsync<T>(key, token: cancellationToken);
            if (value == null || !notExpiredCheck(value))
            {
                var op = new DistributedCacheEntryOptions();
                await cache.SetAsync(key, value = await factory(op), op, cancellationToken);
            }
            return value;
        }
        public static TItem? Get<TItem>(this IDistributedCache cache, string key)
        {
            var str = cache.GetString(key);
            if (str == null)
            {
                return default;
            }
            try
            {
                if (typeof(TItem).IsTuple())
                {
                    return JsonConvert.DeserializeObject<TItem>(str, _settings);
                }
                return JsonConvert.DeserializeObject<TItem>(str, _settings);

            }
            catch (JsonSerializationException)
            {
                return default;
            }
        }
        public static async Task<TItem?> GetAsync<TItem>(this IDistributedCache cache, string key, CancellationToken token = default)
        {
            var str = await cache.GetStringAsync(key, token).ConfigureAwait(false);
            if (str == null)
            {
                return default;
            }
            try
            {
                if (typeof(TItem).IsTuple())
                {
                    return JsonConvert.DeserializeObject<TItem>(str, _settings);
                }
                return JsonConvert.DeserializeObject<TItem>(str, _settings);
            }
            catch (JsonSerializationException)
            {
                return default;
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
            cache.SetString(key, JsonConvert.SerializeObject(value, _settings), options);
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
            return cache.SetStringAsync(key, JsonConvert.SerializeObject(value, _settings), options, token);
        }
        internal static bool IsTuple(this Type tuple)
        {
            if (!tuple.IsGenericType)
                return false;
            return typeof(ITuple).IsAssignableFrom(tuple);
        }
    }
}