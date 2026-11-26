using System;
using System.Collections;
using System.Collections.Generic;

namespace FastSoap.Generators.Models
{

    /// <summary>
    /// Represents an immutable, value-equatable array wrapper for incremental source generator pipeline models.
    /// </summary>
    /// <typeparam name="T">The equatable element type.</typeparam>
    /// <remarks>
    /// Never stores compiler symbols or syntax trees; element equality relies exclusively on <see cref="IEquatable{T}"/>.
    /// </remarks>
    internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
        where T : IEquatable<T>
    {

        #region Fields

        /// <summary>
        /// Gets an empty <see cref="EquatableArray{T}"/>.
        /// </summary>
        internal static readonly EquatableArray<T> Empty = new EquatableArray<T>(Array.Empty<T>());

        private readonly T[]? _array;

        #endregion

        #region Properties

        /// <summary>
        /// Gets the number of elements in the array.
        /// </summary>
        public int Count => _array is null ? 0 : _array.Length;

        /// <summary>
        /// Gets a value indicating whether the array contains no elements.
        /// </summary>
        public bool IsEmpty => _array is null || _array.Length == 0;

        /// <summary>
        /// Gets the element at <paramref name="index"/>.
        /// </summary>
        /// <param name="index">The zero-based index.</param>
        public T this[int index]
        {
            get
            {
                if (_array is null)
                {
                    throw new IndexOutOfRangeException();
                }

                return _array[index];
            }
        }

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new <see cref="EquatableArray{T}"/> wrapping <paramref name="array"/>.
        /// </summary>
        /// <param name="array">The backing immutable array.</param>
        public EquatableArray(T[]? array)
        {
            _array = array;
        }

        #endregion

        #region Public Methods

        /// <inheritdoc />
        public bool Equals(EquatableArray<T> other)
        {
            T[]? left = _array;
            T[]? right = other._array;

            if (ReferenceEquals(left, right))
            {
                return true;
            }

            int leftLength = left is null ? 0 : left.Length;
            int rightLength = right is null ? 0 : right.Length;
            if (leftLength != rightLength)
            {
                return false;
            }

            if (leftLength == 0)
            {
                return true;
            }

            for (int index = 0; index < leftLength; index++)
            {
                if (!left![index].Equals(right![index]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return obj is EquatableArray<T> other && Equals(other);
        }

        /// <summary>
        /// Returns an enumerator over the array elements.
        /// </summary>
        /// <returns>An enumerator.</returns>
        public Enumerator GetEnumerator()
        {
            return new Enumerator(_array);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            if (_array is null || _array.Length == 0)
            {
                return 0;
            }

            unchecked
            {
                int hash = 17;
                for (int index = 0; index < _array.Length; index++)
                {
                    hash = (hash * 31) + _array[index].GetHashCode();
                }

                return hash;
            }
        }

        /// <inheritdoc />
        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            return ((IEnumerable<T>)(_array ?? Array.Empty<T>())).GetEnumerator();
        }

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator()
        {
            return (_array ?? Array.Empty<T>()).GetEnumerator();
        }

        /// <summary>
        /// Compares two <see cref="EquatableArray{T}"/> instances for structural equality.
        /// </summary>
        /// <param name="left">The left array.</param>
        /// <param name="right">The right array.</param>
        /// <returns><see langword="true"/> if structurally equal; otherwise <see langword="false"/>.</returns>
        public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Compares two <see cref="EquatableArray{T}"/> instances for structural inequality.
        /// </summary>
        /// <param name="left">The left array.</param>
        /// <param name="right">The right array.</param>
        /// <returns><see langword="true"/> if not structurally equal; otherwise <see langword="false"/>.</returns>
        public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right)
        {
            return !left.Equals(right);
        }

        #endregion

        /// <summary>
        /// Allocation-free struct enumerator for <see cref="EquatableArray{T}"/>.
        /// </summary>
        internal struct Enumerator
        {

            #region Fields

            private readonly T[]? _array;
            private int _index;

            #endregion

            #region Properties

            /// <summary>
            /// Gets the current element.
            /// </summary>
            public T Current => _array![_index];

            #endregion

            #region Constructors

            internal Enumerator(T[]? array)
            {
                _array = array;
                _index = -1;
            }

            #endregion

            #region Public Methods

            /// <summary>
            /// Advances to the next element.
            /// </summary>
            /// <returns><see langword="true"/> if an element is available; otherwise <see langword="false"/>.</returns>
            public bool MoveNext()
            {
                if (_array is null)
                {
                    return false;
                }

                int next = _index + 1;
                if (next < _array.Length)
                {
                    _index = next;
                    return true;
                }

                return false;
            }

            #endregion

        }

    }

}
