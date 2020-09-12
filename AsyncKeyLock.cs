using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace RecNet.Common.Synchronization
{
    /// <summary>
    /// The <see cref="AsyncKeyLock{TKey}"/> provides the same locking semantics as <see cref="AsyncLock"/> but
    /// dynamically scoped to caller provided keys. When multiple calls are made to lock the same key, then the
    /// behavior is identical to <see cref="AsyncLock"/>. Calls made to lock different keys can all proceed
    /// concurrently, allowing for high throughput.
    /// </summary>
    public class AsyncKeyLock<TKey> where TKey : notnull
    {
        #region Fields

        private readonly RefCountedConcurrentDictionary<TKey, AsyncLock> _activeLocks;
        private readonly ConcurrentBag<AsyncLock> _pool;
        private readonly int _maxPoolSize;

        #endregion

        #region Constructor

        public AsyncKeyLock(int maxPoolSize = 64)
        {
            _activeLocks = new RefCountedConcurrentDictionary<TKey, AsyncLock>(CreateLeasedLock, ReturnLeasedLock);
            _pool = new ConcurrentBag<AsyncLock>();
            _maxPoolSize = maxPoolSize;
        }

        #endregion

        #region APIs

        /// <summary>
        /// Locks the current thread asynchronously.
        /// </summary>
        /// <param name="key">The key identifying the specific object to lock against.</param>
        /// <returns>
        /// The <see cref="Task{IDisposable}"/> that will release the lock.
        /// </returns>
        public Task<IDisposable> LockAsync(TKey key)
        {
            return _activeLocks.Get(key).LockAsync();
        }

        #endregion

        #region RefCountedConcurrentDictionary Callbacks

        private AsyncLock CreateLeasedLock(TKey key)
        {
            if (!_pool.TryTake(out AsyncLock? asyncLock))
            {
                asyncLock = new AsyncLock();
            }
            asyncLock.OnRelease = () => _activeLocks.Release(key);
            return asyncLock;
        }

        private void ReturnLeasedLock(AsyncLock asyncLock)
        {
            if (_pool.Count < _maxPoolSize)
            {
                _pool.Add(asyncLock);
            }
            else
            {
                asyncLock.Dispose();
            }
        }

        #endregion
    }
}
