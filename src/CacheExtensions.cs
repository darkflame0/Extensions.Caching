using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Caching.Memory
{
    public static class CacheExtensions
    {
        static private readonly SemaphoreSlim Sem = new SemaphoreSlim(1, 1);
        public async static Task<TItem> GetOrCreateAtomicAsync<TItem>(this IMemoryCache cache, object key, Func<ICacheEntry, Task<TItem>> factory)
        {
            AsyncLazy<TItem> item;
            await Sem.WaitAsync().ConfigureAwait(false);
            try
            {
                item = cache.GetOrCreate(key, e =>
                    new AsyncLazy<TItem>(() => factory(e))
                );
            }
            finally
            {
                Sem.Release();
            }
            try
            {
                return await item.Value.ConfigureAwait(false);
            }
            catch
            {
                cache.Remove(key);
                throw;
            }
        }
        public static TItem GetOrCreateAtomic<TItem>(this IMemoryCache cache, object key, Func<ICacheEntry, TItem> factory)
        {
            Lazy<TItem> item;
            Sem.Wait();
            try
            {
                item = cache.GetOrCreate(key, e =>
                    new Lazy<TItem>(() => factory(e))
                );
            }
            finally
            {
                Sem.Release();
            }
            try
            {
                return item.Value;
            }
            catch
            {
                cache.Remove(key);
                throw;
            }
        }
    }
}
