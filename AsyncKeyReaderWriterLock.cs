using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace RecNet.Common.Synchronization
{
    /// <summary>
    /// The <see cref="AsyncKeyReaderWriterLock{TKey}"/> provides the same locking semantics as
    /// <see cref="AsyncReaderWriterLock"/> but dynamically scoped to caller provided keys. When multiple
    /// calls are made to lock the same key, then the behavior is identical to <see cref="AsyncReaderWriterLock"/>.
    /// Calls made to lock different keys can all proceed concurrently, allowing for high throughput.
    /// </summary>
    public class AsyncKeyReaderWriterLock<TKey> where TKey : notnull
    {
        #region Fields

        private readonly RefCountedConcurrentDictionary<TKey, AsyncReaderWriterLock> _activeLocks;
        private readonly ConcurrentBag<AsyncReaderWriterLock> _pool;
        private readonly int _maxPoolSize;

        #endregion

        #region Constructors

        public AsyncKeyReaderWriterLock(int maxPoolSize = 64)
        {
            _activeLocks = new RefCountedConcurrentDictionary<TKey, AsyncReaderWriterLock>(CreateLeasedLock, ReturnLeasedLock);
            _pool = new ConcurrentBag<AsyncReaderWriterLock>();
            _maxPoolSize = maxPoolSize;
        }

        #endregion

        #region APIs

        /// <summary>
        /// Locks the current thread in read mode asynchronously.
        /// </summary>
        /// <param name="key">The key identifying the specific object to lock against.</param>
        /// <returns>
        /// The <see cref="Task{IDisposable}"/> that will release the lock.
        /// </returns>
        public Task<IDisposable> ReaderLockAsync(TKey key)
        {
            return _activeLocks.Get(key).ReaderLockAsync();
        }

        /// <summary>
        /// Locks the current thread in write mode asynchronously.
        /// </summary>
        /// <param name="key">The key identifying the specific object to lock against.</param>
        /// <returns>
        /// The <see cref="Task{IDisposable}"/> that will release the lock.
        /// </returns>
        public Task<IDisposable> WriterLockAsync(TKey key)
        {
            return _activeLocks.Get(key).WriterLockAsync();
        }

        #endregion

        #region RefCountedConcurrentDictionary Callbacks

        private AsyncReaderWriterLock CreateLeasedLock(TKey key)
        {
            if (!_pool.TryTake(out AsyncReaderWriterLock? asyncLock))
            {
                asyncLock = new AsyncReaderWriterLock();
            }
            asyncLock.OnRelease = () => _activeLocks.Release(key);
            return asyncLock;
        }

        private void ReturnLeasedLock(AsyncReaderWriterLock asyncLock)
        {
            if (_pool.Count < _maxPoolSize)
            {
                _pool.Add(asyncLock);
            }
        }

        #endregion
    }
}
