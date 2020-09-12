﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace RecNet.Common.Synchronization
{
    /// <summary>
    /// Represents a thread-safe collection of reference-counted key/value pairs that can be accessed by multiple
    /// threads concurrently. Values that don't yet exist are automatically created using a caller supplied
    /// value factory method, and when their final refcount is released they are removed from the dictionary.
    /// </summary>
    public class RefCountedConcurrentDictionary<TKey, TValue>
        where TKey : notnull
        where TValue : class
    {
        #region Types

        /// <summary>
        /// Simple immutable tuple that combines a <typeparamref name="TValue"/> instance with a ref count integer.
        /// </summary>
        private class RefCountedValue : IEquatable<RefCountedValue>
        {
            public readonly TValue Value;
            public readonly int RefCount;

            public RefCountedValue(TValue value, int refCount)
            {
                Value = value;
                RefCount = refCount;
            }

            public bool Equals([AllowNull] RefCountedValue other) => (other != null) && (RefCount == other.RefCount) && EqualityComparer<TValue>.Default.Equals(Value, other.Value);
            public override bool Equals(object? obj) => (obj is RefCountedValue other) && Equals(other);
            public override int GetHashCode() => ((RefCount << 5) + RefCount) ^ Value.GetHashCode();
        }

        #endregion

        #region Fields

        private readonly ConcurrentDictionary<TKey, RefCountedValue> _dictionary;
        private readonly Func<TKey, TValue> _valueFactory;
        private readonly Action<TValue>? _valueReleaser;

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="RefCountedConcurrentDictionary{TKey, TValue}"/> class that is empty,
        /// has the default concurrency level, has the default initial capacity, and uses the default comparer for the key type.
        /// </summary>
        /// <param name="valueFactory">Factory method that generates a new <typeparamref name="TValue"/> for a given <typeparamref name="TKey"/>.</param>
        /// <param name="valueReleaser">Optional callback that is used to cleanup <typeparamref name="TValue"/>s after their final ref count is released.</param>
        public RefCountedConcurrentDictionary(Func<TKey, TValue> valueFactory, Action<TValue>? valueReleaser = null)
            : this(new ConcurrentDictionary<TKey, RefCountedValue>(), valueFactory, valueReleaser) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="RefCountedConcurrentDictionary{TKey, TValue}"/> class that is empty,
        /// has the default concurrency level and capacity,, and uses the specified  <see cref="IEqualityComparer{TKey}"/>.
        /// </summary>
        /// <param name="comparer">The <see cref="IEqualityComparer{TKey}"/> implementation to use when comparing keys.</param>
        /// <param name="valueFactory">Factory method that generates a new <typeparamref name="TValue"/> for a given <typeparamref name="TKey"/>.</param>
        /// <param name="valueReleaser">Optional callback that is used to cleanup <typeparamref name="TValue"/>s after their final ref count is released.</param>
        public RefCountedConcurrentDictionary(IEqualityComparer<TKey> comparer, Func<TKey, TValue> valueFactory, Action<TValue>? valueReleaser)
            : this(new ConcurrentDictionary<TKey, RefCountedValue>(comparer), valueFactory, valueReleaser) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="RefCountedConcurrentDictionary{TKey, TValue}"/> class that is empty,
        /// has the specified concurrency level and capacity, and uses the default comparer for the key type.
        /// </summary>
        /// <param name="concurrencyLevel">The estimated number of threads that will access the <see cref="RefCountedConcurrentDictionary{TKey, TValue}"/> concurrently</param>
        /// <param name="capacity">The initial number of elements that the <see cref="RefCountedConcurrentDictionary{TKey, TValue}"/> can contain.</param>
        /// <param name="valueFactory">Factory method that generates a new <typeparamref name="TValue"/> for a given <typeparamref name="TKey"/>.</param>
        /// <param name="valueReleaser">Optional callback that is used to cleanup <typeparamref name="TValue"/>s after their final ref count is released.</param>
        public RefCountedConcurrentDictionary(int concurrencyLevel, int capacity, Func<TKey, TValue> valueFactory, Action<TValue>? valueReleaser = null)
            : this(new ConcurrentDictionary<TKey, RefCountedValue>(concurrencyLevel, capacity), valueFactory, valueReleaser) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="RefCountedConcurrentDictionary{TKey, TValue}"/> class that is empty,
        /// has the specified concurrency level, has the specified initial capacity, and uses the specified 
        /// <see cref="IEqualityComparer{TKey}"/>.
        /// </summary>
        /// <param name="concurrencyLevel">The estimated number of threads that will access the <see cref="RefCountedConcurrentDictionary{TKey, TValue}"/> concurrently</param>
        /// <param name="capacity">The initial number of elements that the <see cref="RefCountedConcurrentDictionary{TKey, TValue}"/> can contain.</param>
        /// <param name="comparer">The <see cref="IEqualityComparer{TKey}"/> implementation to use when comparing keys.</param>
        /// <param name="valueFactory">Factory method that generates a new <typeparamref name="TValue"/> for a given <typeparamref name="TKey"/>.</param>
        /// <param name="valueReleaser">Optional callback that is used to cleanup <typeparamref name="TValue"/>s after their final ref count is released.</param>
        public RefCountedConcurrentDictionary(int concurrencyLevel, int capacity, IEqualityComparer<TKey> comparer, Func<TKey, TValue> valueFactory, Action<TValue>? valueReleaser)
            : this(new ConcurrentDictionary<TKey, RefCountedValue>(concurrencyLevel, capacity, comparer), valueFactory, valueReleaser) { }

        private RefCountedConcurrentDictionary(ConcurrentDictionary<TKey, RefCountedValue> dictionary, Func<TKey, TValue> valueFactory, Action<TValue>? valueReleaser)
        {
            _dictionary = dictionary ?? throw new ArgumentNullException(nameof(dictionary));
            _valueFactory = valueFactory ?? throw new ArgumentNullException(nameof(valueFactory));
            _valueReleaser = valueReleaser;
        }

        #endregion

        #region APIs

        /// <summary>
        /// Obtains a reference to the value corresponding to the specified key. If no such value exists in the
        /// dictionary, then a new value is generated using the value factory method supplied in the constructor.
        /// To prevent leaks, this reference MUST be released via <see cref="Release(TKey)"/>.
        /// </summary>
        /// <param name="key">The key of the element to add ref.</param>
        /// <returns>The referenced object.</returns>
        public TValue Get(TKey key)
        {
            while (true)
            {
                if (_dictionary.TryGetValue(key, out var refCountedValue))
                {
                    // Increment ref count
                    if (_dictionary.TryUpdate(key, new RefCountedValue(refCountedValue.Value, refCountedValue.RefCount + 1), refCountedValue))
                    {
                        return refCountedValue.Value;
                    }
                }
                else
                {
                    // Add new value to dictionary
                    TValue value = _valueFactory(key);

                    if (_dictionary.TryAdd(key, new RefCountedValue(value, 1)))
                    {
                        return value;
                    }
                    else
                    {
                        _valueReleaser?.Invoke(value);
                    }
                }
            }
        }

        /// <summary>
        /// Releases a reference to the value corresponding to the specified key. If this reference was the last
        /// remaining reference to the value, then the value is removed from the dictionary, and the optional value
        /// releaser callback is invoked.
        /// </summary>
        /// <param name="key">THe key of the element to release.</param>
        public void Release(TKey key)
        {
            while (true)
            {
                if (!_dictionary.TryGetValue(key, out var refCountedValue))
                {
                    // This is BAD. It indicates a ref counting problem where someone is either double-releasing,
                    // or they're releasing a key that they never obtained in the first place!!
                    throw new InvalidOperationException($"Tried to release value that doesn't exist in the dictionary ({key})!");
                }

                // If we're releasing the last reference, then try to remove the value from the dictionary.
                // Otherwise, try to decrement the reference count.
                if (refCountedValue.RefCount == 1)
                {
                    // Remove from dictionary.  We use the ICollection<>.Remove() method instead of the ConcurrentDictionary.TryRemove()
                    // because this specific API will only succeed if the value hasn't been changed by another thread.
                    if (((ICollection<KeyValuePair<TKey, RefCountedValue>>)_dictionary).Remove(new KeyValuePair<TKey, RefCountedValue>(key, refCountedValue)))
                    {
                        _valueReleaser?.Invoke(refCountedValue.Value);
                        return;
                    }
                }
                else
                {
                    // Decrement ref count
                    if (_dictionary.TryUpdate(key, new RefCountedValue(refCountedValue.Value, refCountedValue.RefCount - 1), refCountedValue))
                    {
                        return;
                    }
                }
            }
        }

        #endregion
    }
}
