using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Caching.Memory
{
    public static class CacheExtensions
    {
        private static readonly ConcurrentDictionary<int, SemaphoreSlim> _semaphores = new ConcurrentDictionary<int, SemaphoreSlim>();

        public static async Task<T> GetOrCreateAtomicAsync<T>(this IMemoryCache memoryCache, object key, Func<ICacheEntry, Task<T>> factory, Func<T, bool>? notExpiredCheck = null, TimeSpan? waitTime = default)
        {
            static bool DefaultCheck(T data) => true;
            var expired = false;
            notExpiredCheck ??= DefaultCheck;
            if (memoryCache.TryGetValue(key, out T value))
            {
                if (notExpiredCheck(value))
                {
                    return value;
                }
                else
                {
                    expired = true;
                }

            }

            var isOwner = false;
            var semaphoreKey = (memoryCache, key).GetHashCode();
            if (!_semaphores.TryGetValue(semaphoreKey, out var semaphore))
            {
                SemaphoreSlim? createdSemaphore = null;
                semaphore = _semaphores.GetOrAdd(semaphoreKey, k => createdSemaphore = new SemaphoreSlim(1)); // Try to add the value, this is not atomic, so multiple semaphores could be created, but just one will be stored!

                if (createdSemaphore != semaphore)
                    createdSemaphore?.Dispose(); // This semaphore was not the one that made it into the dictionary, will not be used!
                else
                    isOwner = true;
            }

            var get = await semaphore.WaitAsync(waitTime ?? Timeout.InfiniteTimeSpan).ConfigureAwait(false); // Await the semaphore!
            if (!get)
            {
                if (expired)
                {
                    return value;
                }
                throw new OperationCanceledException();
            }

            try
            {
                if (!memoryCache.TryGetValue(key, out value) || expired)
                {
                    var entry = memoryCache.CreateEntry(key);
                    entry.SetValue(value = await factory(entry));
                    entry.Dispose();
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
    }
}
