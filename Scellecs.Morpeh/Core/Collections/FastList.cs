namespace Scellecs.Morpeh.Collections {
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using Unity.IL2CPP.CompilerServices;

    [Serializable]
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    [Il2CppSetOption(Option.DivideByZeroChecks, false)]
    public sealed class FastList<T> {
        public T[] data;
        public int length;
        public int capacity;

        public EqualityComparer<T> comparer;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FastList() {
            this.capacity = 4;
            this.data     = new T[this.capacity];
            this.length   = 0;

            this.comparer = EqualityComparer<T>.Default;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FastList(int capacity) {
            this.capacity = HashHelpers.GetCapacity(capacity) + 1;
            this.data     = new T[this.capacity];
            this.length   = 0;

            this.comparer = EqualityComparer<T>.Default;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FastList(FastList<T> other) {
            this.capacity = other.capacity;
            this.data     = new T[this.capacity];
            this.length   = other.length;
            Array.Copy(other.data, 0, this.data, 0, this.length);

            this.comparer = other.comparer;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Enumerator GetEnumerator() {
            Enumerator e;
            e.length = this.length;
            e.list    = this;
            e.current = default;
            e.index   = 0;
            return e;
        }

        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [Il2CppSetOption(Option.DivideByZeroChecks, false)]
        public struct ResultSwap {
            public int oldIndex;
            public int newIndex;
        }

        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [Il2CppSetOption(Option.DivideByZeroChecks, false)]
        public struct Enumerator {
            public FastList<T> list;

            public int length;
            public T   current;
            public int index;

            public bool MoveNext() {
                this.length = this.list.length;
                
                if (this.index >= this.length) {
                    return false;
                }

                this.current = this.list.data[this.index++];

                return true;
            }

            public T Current {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => this.current;
            }
        }
    }
}