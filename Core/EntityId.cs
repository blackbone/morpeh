﻿namespace Scellecs.Morpeh {
    using System;
    using Unity.IL2CPP.CompilerServices;

#if !MORPEH_NON_SERIALIZED
    [Serializable]
#endif
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    [Il2CppSetOption(Option.DivideByZeroChecks, false)]
    public readonly struct EntityId : IEquatable<EntityId> {
        internal static readonly EntityId Default = new EntityId(-1, -1);

        internal readonly int id;
        internal readonly int gen;

        public EntityId(int id, int gen) {
            this.id  = id;
            this.gen = gen;
        }

        public bool Equals(EntityId other) {
            return this.id == other.id && this.gen == other.gen;
        }

        public override bool Equals(object obj) {
            return obj is EntityId other && this.Equals(other);
        }

        public override int GetHashCode() {
            unchecked {
                return (this.id * 397) ^ this.gen;
            }
        }

        public static bool operator ==(EntityId left, EntityId right) {
            return left.Equals(right);
        }

        public static bool operator !=(EntityId left, EntityId right) {
            return !(left == right);
        }

        public override string ToString() {
            return $"EntityId(id={this.id}, gen={this.gen})";
        }

        public static EntityId Invalid => new EntityId(-1, -1);
    }
}
