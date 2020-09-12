# AsyncLocks
Small library of async locking synchronization primitives.

## AsyncLock
An asynchronous locker that uses an IDisposable pattern for releasing the lock.

```
var myLock = new AsyncLock();

using (await myLock.LockAsync())
{
    // Lock is now held
}
```

## AsyncReaderWriterLock
An asynchronous locker that provides read and write locking policies and uses an IDisposable
pattern for releasing the lock.

```
var myLock = new AsyncReaderWriterLock();

using (await myLock.ReaderLockAsync())
{
    // Reader lock is now held. Multiple callers can concurrently hold the reader lock
}

using (await myLock.WriterLockAsync())
{
    // Writer lock is now held. No other callers can hold reader or writer lock right now.
}
```

## AsyncKeyLock
Provides the same locking semantics as `AsyncLock` but dynamically scoped to caller provided
keys. When multiple calls are made to lock the same key, then the behavior is identical to
`AsyncLock`. Calls made to lock different keys can all proceed concurrently, allowing for
high throughput.

```
var myLock = new AsyncKeyLock<string>();

using (await myLock.Lock("foo"))
{
    // Lock on "foo" is now held. No other callers can hold the lock on "foo" right now,
    // but calls to lock other keys can proceed unblocked.
}
```

## AsyncKeyReaderWriterLock
Provides the same locking semantics as `AsyncReaderWriterLock` but dynamically scoped to
caller provided keys. When multiple calls are made to lock the same key, then the behavior
is identical to `AsyncReaderWriterLock`. Calls made to lock different keys can all proceed
concurrently, allowing for high throughput.

```
var myLock = new AsyncKeyReaderWriterLock<string>();

using (await myLock.ReaderLockAsync("foo"))
{
    // Reader lock on "foo" is now held. Other readers can concurrently hold the locks on
    // "foo", and calls to lock other keys can all proceed unblocked.
}

using (await myLock.WriterLockAsync("foo"))
{
    // Writer lock on "foo" is now held. No other callers can hold reader or writer locks
    // on "foo" right now, but calls to lock other keys can all proceed unblocked.
}
```

## RefCountedConcurrentDictionary
Represents a thread-safe collection of reference-counted key/value pairs that can be accessed
by multiple threads concurrently. Values that don't yet exist are automatically created using
a caller supplied value factory method, and when their final refcount is released they are
removed from the dictionary.

```
var dict = new RefCountedConcurrentDictionary<string, object>(
    valueFactory: (id) => new object(),
    valueReleaser: (obj) => {});

// Value factory is invoked to create the value for "foo"
var value = dict.Get("foo");

// Refcount on "foo" is incremented
var value2 = dict.Get("foo");

// Refcount on "foo" is decremented
dict.Release("foo");

// Value is removed from the dictionary and valueReleaser callback is invoked
dict.Release("foo");
```
