using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RecNet.Common.Synchronization
{
    /// <summary>
    /// An asynchronous locker that provides read and write locking policies and uses an IDisposable
    /// pattern for releasing the lock.
    /// <para>
    /// This is based on the following blog post:
    /// https://devblogs.microsoft.com/pfxteam/building-async-coordination-primitives-part-7-asyncreaderwriterlock/
    /// </para>
    /// </summary>
    public class AsyncReaderWriterLock
    {
        #region Types

        private sealed class Releaser : IDisposable
        {
            private readonly AsyncReaderWriterLock _toRelease;
            private readonly bool _writer;
            internal Releaser(AsyncReaderWriterLock toRelease, bool writer)
            {
                _toRelease = toRelease;
                _writer = writer;
            }

            public void Dispose()
            {
                if (_writer)
                {
                    _toRelease.WriterRelease();
                }
                else
                {
                    _toRelease.ReaderRelease();
                }
            }
        }

        #endregion

        #region Fields

        private readonly IDisposable _writerReleaser;
        private readonly IDisposable _readerReleaser;
        private readonly Task<IDisposable> _writerReleaserTask;
        private readonly Task<IDisposable> _readerReleaserTask;
        private readonly Queue<TaskCompletionSource<IDisposable>> _waitingWriters;
        private TaskCompletionSource<IDisposable>? _waitingReader;
        private int _readersWaiting;

        /// <summary>
        /// Tracks the current status of the lock:
        /// 0 == lock is unheld
        /// -1 == lock is held by a single writer
        /// >0 == lock is held by this number of concurrent readers
        /// </summary>
        private int _status;

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets the callback that should be invoked whenever this lock is released.
        /// </summary>
        public Action? OnRelease { get; set; }

        #endregion

        #region Constructor

        /// <summary>
        /// Initializes a new instance of the <see cref="AsyncReaderWriterLock"/> class.
        /// </summary>
        public AsyncReaderWriterLock()
        {
            _writerReleaser = new Releaser(this, true);
            _readerReleaser = new Releaser(this, false);
            _writerReleaserTask = Task.FromResult(_writerReleaser);
            _readerReleaserTask = Task.FromResult(_readerReleaser);
            _waitingWriters = new Queue<TaskCompletionSource<IDisposable>>();
            _waitingReader = null;
            _readersWaiting = 0;
            _status = 0;
        }

        #endregion

        #region APIs

        /// <summary>
        /// Asynchronously obtains the lock in shared reader mode. Dispose the returned <see cref="IDisposable"/>
        /// to release the lock.
        /// </summary>
        /// <returns>
        /// The <see cref="Task{IDisposable}"/> that will release the lock.
        /// </returns>
        public Task<IDisposable> ReaderLockAsync()
        {
            lock (_waitingWriters)
            {
                if (_status >= 0 && _waitingWriters.Count == 0)
                {
                    // Lock is not held by a writer and no writers are waiting, so allow this reader to obtain
                    // the lock immediately and return the pre-allocated reader releaser.
                    ++_status;
                    return _readerReleaserTask;
                }
                else
                {
                    // This reader has to wait to obtain the lock.  Lazy instantiate the _waitingReader tcs, and
                    // then return a unique continuation of that tcs (this ensures that all waiting readers can be
                    // released simultaneously).
                    ++_readersWaiting;
                    if (_waitingReader == null)
                    {
                        _waitingReader = new TaskCompletionSource<IDisposable>();
                    }
                    return _waitingReader.Task.ContinueWith(t => t.Result, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                }
            }
        }

        /// <summary>
        /// Asynchronously obtains the lock in exclusive writer mode. Dispose the returned <see cref="IDisposable"/>
        /// to release the lock.
        /// </summary>
        /// <returns>
        /// The <see cref="Task{IDisposable}"/> that will release the lock.
        /// </returns>
        public Task<IDisposable> WriterLockAsync()
        {
            lock (_waitingWriters)
            {
                if (_status == 0)
                {
                    // Lock is currently unheld, so allow this writer to obtain the lock immediately and return the
                    // pre-allocated writer releaser.
                    _status = -1;
                    return _writerReleaserTask;
                }
                else
                {
                    // This writer has to wait to obtain the lock. Create a new tcs for this writer, add it to the 
                    // queue of waiting writers, and return the task.
                    var waiter = new TaskCompletionSource<IDisposable>();
                    _waitingWriters.Enqueue(waiter);
                    return waiter.Task;
                }
            }
        }

        private void ReaderRelease()
        {
            try
            {
                TaskCompletionSource<IDisposable>? toWake = null;

                lock (_waitingWriters)
                {
                    --_status;
                    if (_status == 0 && _waitingWriters.Count > 0)
                    {
                        // The lock is now unheld, and there's a writer waiting, so give the lock to the first writer in the queue.
                        _status = -1;
                        toWake = _waitingWriters.Dequeue();
                    }
                }

                toWake?.SetResult(_writerReleaser);
            }
            finally
            {
                OnRelease?.Invoke();
            }
        }

        private void WriterRelease()
        {
            try
            {
                TaskCompletionSource<IDisposable>? toWake = null;
                bool toWakeIsWriter = false;

                lock (_waitingWriters)
                {
                    if (_waitingWriters.Count > 0)
                    {
                        // There's another writer waiting, so pass the lock on to the next writer.
                        toWake = _waitingWriters.Dequeue();
                        toWakeIsWriter = true;
                    }
                    else if (_readersWaiting > 0)
                    {
                        // There are readers waiting. Wake them all up at the same time since they can all concurrently
                        // hold the reader lock.
                        toWake = _waitingReader;
                        _status = _readersWaiting;
                        _readersWaiting = 0;
                        _waitingReader = null;
                    }
                    else
                    {
                        // Nobody is waiting, so the lock is now unheld.
                        _status = 0;
                    }
                }

                toWake?.SetResult(toWakeIsWriter ? _writerReleaser : _readerReleaser);
            }
            finally
            {
                OnRelease?.Invoke();
            }
        }

        #endregion
    }
}
