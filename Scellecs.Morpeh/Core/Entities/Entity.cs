namespace Scellecs.Morpeh {
    using System;
    using Sirenix.OdinInspector;
    using Unity.IL2CPP.CompilerServices;
    using System.Runtime.CompilerServices;
    
#if !MORPEH_NON_SERIALIZED
    [Serializable]
#endif
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    [Il2CppSetOption(Option.DivideByZeroChecks, false)]
    public readonly struct Entity : IEquatable<Entity> {
        internal readonly long value;
        
        // [ Id: 32 bits | Generation: 16 bits | WorldId: 8 bits | Unused: 8 bits ]
        internal Entity(int worldId, int id, ushort generation) {
            value = ((id & 0xFFFFFFFFL) << 32) | ((generation & 0xFFFFL) << 16) | ((worldId & 0xFFL) << 8);
        }

        [ShowInInspector]
        public int Id {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (int)((value >> 32) & 0xFFFFFFFF);
        }

        [ShowInInspector]
        public ushort Generation {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (ushort)((value >> 16) & 0xFFFF);
        }

        [ShowInInspector]
        public int WorldId {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (int)((value >> 8) & 0xFF);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(Entity lhs, Entity rhs) {
            return lhs.value == rhs.value;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(Entity lhs, Entity rhs) {
            return lhs.value != rhs.value;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(Entity other) {
            return this.value == other.value;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object obj) {
            return obj is Entity other && this.Equals(other);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode() {
            return this.value.GetHashCode();
        }
        
        public int CompareTo(Entity other) {
            return this.Id.CompareTo(other.Id);
        }

        public override string ToString() {
            return $"Entity: Id={this.Id}, Generation={this.Generation}, WorldId={this.WorldId}";
        }
    }
}