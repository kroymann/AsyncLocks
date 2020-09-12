using System;
using System.Threading;
using System.Threading.Tasks;

namespace RecNet.Common.Synchronization
{
    /// <summary>
    /// An asynchronous locker that uses an IDisposable pattern for releasing the lock.
    /// </summary>
    public class AsyncLock : IDisposable
    {
        #region Types

        private sealed class Releaser : IDisposable
        {
            private readonly AsyncLock _toRelease;
            internal Releaser(AsyncLock toRelease) => _toRelease = toRelease;
            public void Dispose() => _toRelease?.Release();
        }

        #endregion

        #region Fields

        private readonly SemaphoreSlim _semaphore;
#pragma warning disable IDE0069 // Disposable fields should be disposed
        private readonly IDisposable _releaser;
#pragma warning restore IDE0069 // Disposable fields should be disposed
        private readonly Task<IDisposable> _releaserTask;
        private bool _disposed = false;

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets the callback that should be invoked whenever this lock is released.
        /// </summary>
        public Action? OnRelease { get; set; }

        #endregion

        #region Constructor

        /// <summary>
        /// Initializes a new instance of the <see cref="AsyncLock"/> class.
        /// </summary>
        public AsyncLock()
        {
            _semaphore = new SemaphoreSlim(1, 1);
            _releaser = new Releaser(this);
            _releaserTask = Task.FromResult(_releaser);
        }

        #endregion

        #region APIs

        /// <summary>
        /// Asynchronously obtains the lock. Dispose the returned <see cref="IDisposable"/> to release the lock.
        /// </summary>
        /// <returns>
        /// The <see cref="Task{IDisposable}"/> that will release the lock.
        /// </returns>
        public Task<IDisposable> LockAsync()
        {
            var wait = _semaphore.WaitAsync();

            // No-allocation fast path when the semaphore wait completed synchronously
            return (wait.Status == TaskStatus.RanToCompletion)
                ? _releaserTask
                : AwaitThenReturn(wait, _releaser);

            static async Task<IDisposable> AwaitThenReturn(Task t, IDisposable r)
            {
                await t;
                return r;
            }
        }

        private void Release()
        {
            try
            {
                _semaphore.Release();
            }
            finally
            {
                OnRelease?.Invoke();
            }
        }

        #endregion

        #region IDisposable

        /// <summary>
        /// Releases all resources used by the current instance of the <see cref="AsyncLock"/> class.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                _semaphore.Dispose();
                _disposed = true;
            }
        }

        #endregion
    }
}
