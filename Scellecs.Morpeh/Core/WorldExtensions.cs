#if UNITY_EDITOR
#define MORPEH_DEBUG
#define MORPEH_PROFILING
#endif
#if !MORPEH_DEBUG
#define MORPEH_DEBUG_DISABLED
#endif

#if ENABLE_MONO || ENABLE_IL2CPP
#define MORPEH_UNITY
#endif

namespace Scellecs.Morpeh {
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using Collections;
    using JetBrains.Annotations;
#if MORPEH_BURST
    using Unity.Collections;
#endif
    using Unity.IL2CPP.CompilerServices;
    using UnityEngine;

    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    [Il2CppSetOption(Option.DivideByZeroChecks, false)]
    public static class WorldExtensions {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Ctor(this World world) {
            world.threadIdLock = System.Threading.Thread.CurrentThread.ManagedThreadId;
            
            world.systemsGroups    = new SortedList<int, SystemsGroup>();
            world.newSystemsGroups = new SortedList<int, SystemsGroup>();

            world.pluginSystemsGroups    = new FastList<SystemsGroup>();
            world.newPluginSystemsGroups = new FastList<SystemsGroup>();

            world.Filter           = new FilterBuilder{ world = world };
            world.filters          = new FastList<Filter>();
            world.filtersLookup    = new LongHashMap<LongHashMap<Filter>>();
            
#if MORPEH_BURST
            world.tempArrays = new FastList<NativeArray<Entity>>();
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static World Initialize(this World world) {
            var added = false;
            var id    = -1;

            for (int i = 0, length = World.worlds.length; i < length; i++) {
                if (World.worlds.data[i] == null) {
                    added                = true;
                    id                   = i;
                    World.worlds.data[i] = world;
                    break;
                }
            }
            if (added == false) {
                World.worlds.Add(world);
            }
            world.identifier        = added ? id : World.worlds.length - 1;
            world.freeEntityIDs     = new IntStack();
            world.nextFreeEntityIDs = new IntStack();
            world.stashes           = new Stash[Constants.DEFAULT_WORLD_STASHES_CAPACITY];

            world.entitiesCount    = 0;
            world.entitiesLength   = 0;
            world.entitiesCapacity = Constants.DEFAULT_WORLD_ENTITIES_CAPACITY;
            
            world.entities = new EntityData[world.entitiesCapacity];
            for (var i = 0; i < world.entitiesCapacity; i++) {
                world.entities[i].Initialize();
            }
            world.entitiesGens = new ushort[world.entitiesCapacity];
            world.dirtyEntities    = new IntSparseSet(world.entitiesCapacity);
            world.disposedEntities = new IntSparseSet(world.entitiesCapacity);

            world.archetypes         = new LongHashMap<Archetype>();
            world.archetypesCount    = 0;
            
            world.archetypePool = new ArchetypePool(32);
            world.emptyArchetypes = new FastList<Archetype>();

            world.componentsToFiltersRelation = new ComponentsToFiltersRelation(256);

            if (World.plugins != null) {
                foreach (var plugin in World.plugins) {
#if MORPEH_DEBUG
                    try {
#endif
                        plugin.Initialize(world);
#if MORPEH_DEBUG
                    }
                    catch (Exception e) {
                        MLogger.LogError($"Can not initialize world plugin {plugin.GetType()}");
                        MLogger.LogException(e);
                    }
#endif
                }
            }

            return world;
        }

#if MORPEH_UNITY && !MORPEH_DISABLE_AUTOINITIALIZATION
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
#endif
        [PublicAPI]
        public static void InitializationDefaultWorld() {
            foreach (var world in World.worlds) {
                if (!world.IsNullOrDisposed()) {
                    world.Dispose();
                }
            }
            World.worlds.Clear();
            var defaultWorld = World.Create();
            defaultWorld.UpdateByUnity = true;
#if MORPEH_UNITY
            var go = new GameObject {
                name      = "MORPEH_UNITY_RUNTIME_HELPER",
                hideFlags = HideFlags.DontSaveInEditor
            };
            go.AddComponent<UnityRuntimeHelper>();
            go.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(go);
#endif
        }

        [PublicAPI]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void JobsComplete(this World world) {
            world.ThreadSafetyCheck();
#if MORPEH_BURST
            world.JobHandle.Complete();
            foreach (var array in world.tempArrays) {
                array.Dispose();
            }
            world.tempArrays.Clear();
#endif
        }

        [PublicAPI]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SystemsGroup CreateSystemsGroup(this World world) {
            world.ThreadSafetyCheck();
            return new SystemsGroup(world);
        }

        [PublicAPI]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void AddSystemsGroup(this World world, int order, SystemsGroup systemsGroup) {
            world.ThreadSafetyCheck();
            
            world.newSystemsGroups.Add(order, systemsGroup);
        }

        [PublicAPI]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void RemoveSystemsGroup(this World world, SystemsGroup systemsGroup) {
            world.ThreadSafetyCheck();
            
            systemsGroup.Dispose();
            if (world.systemsGroups.ContainsValue(systemsGroup)) {
                world.systemsGroups.RemoveAt(world.systemsGroups.IndexOfValue(systemsGroup));
            }
            else if (world.newSystemsGroups.ContainsValue(systemsGroup)) {
                world.newSystemsGroups.RemoveAt(world.newSystemsGroups.IndexOfValue(systemsGroup));
            }
        }

        [PublicAPI]
        public static void Commit(this World world) {
            MLogger.LogTrace("[WorldExtensions] Commit");
            
            world.ThreadSafetyCheck();
            
#if MORPEH_DEBUG
            if (world.iteratorLevel > 0) {
                MLogger.LogError("You can not call world.Commit() inside Filter foreach loop. Place it outside of foreach block. ");
                return;
            }
#endif
            
            world.newMetrics.commits++;
            MLogger.BeginSample("World.Commit()");
#if MORPEH_DEBUG && MORPEH_BURST
            if (world.dirtyEntities.count > 0 && (world.JobHandle.IsCompleted == false)) {
                MLogger.LogError("You have changed entities before all scheduled jobs are completed. This may lead to unexpected behavior or crash. Jobs will be forced.");
                world.JobsComplete();
            }
#endif
            if (world.disposedEntities.count > 0) {
                world.CompleteDisposals();
            }
            
            if (world.dirtyEntities.count > 0) {
                world.newMetrics.migrations += world.dirtyEntities.count;
                world.ApplyTransientChanges();
            }
            
            if (world.nextFreeEntityIDs.length > 0) {
                world.PushFreeIds();
            }

            if (world.emptyArchetypes.length > 0) {
                world.ClearEmptyArchetypes();
            }

            MLogger.EndSample();
            
            MLogger.LogTrace("[WorldExtensions] Commit done");
        }
        
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void CompleteDisposals(this World world) {
            foreach (var entityId in world.disposedEntities) {
                world.CompleteEntityDisposal(entityId, ref world.entities[entityId]);
            }
            
            world.disposedEntities.Clear();
        }
        
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void ApplyTransientChanges(this World world) {
            var clearedEntities = 0;
            
            foreach (var entityId in world.dirtyEntities) {
                ref var entityData = ref world.entities[entityId];
                
                if (entityData.nextArchetypeHash == default) {
                    world.CompleteEntityDisposal(entityId, ref entityData);
                    world.IncrementGeneration(entityId);
                    clearedEntities++;
                } else {
                    world.ApplyTransientChanges(world.GetEntityAtIndex(entityId), ref entityData);
                }
            }

            world.entitiesCount -= clearedEntities;
            world.dirtyEntities.Clear();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void PushFreeIds(this World world) {
            world.freeEntityIDs.PushRange(world.nextFreeEntityIDs);
            world.nextFreeEntityIDs.Clear();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void ClearEmptyArchetypes(this World world) {
            foreach (var archetype in world.emptyArchetypes) {
                if (!archetype.IsEmpty()) {
                    MLogger.LogTrace($"[WorldExtensions] Archetype {archetype.hash} is not empty after complete migration of entities");
                    continue;
                }
                
                MLogger.LogTrace($"[WorldExtensions] Remove archetype {archetype.hash}");
                
                foreach (var idx in archetype.filters) {
                    var filter = archetype.filters.GetValueByIndex(idx);
                    filter.RemoveArchetype(archetype);
                }
                
                archetype.ClearFilters();
                archetype.components.Clear();
                
                world.archetypes.Remove(archetype.hash.GetValue(), out _);
                world.archetypesCount--;
                world.archetypePool.Return(archetype);
            }
            
            world.emptyArchetypes.Clear();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void TransientChangeAddComponent(this World world, Entity entity, ref TypeInfo typeInfo) {
            ref var entityData = ref world.entities[entity.Id];
            
            if (!FilterDuplicateOperationType(ref entityData, typeInfo.id)) {
                if (entityData.changesCount == entityData.changes.Length) {
                    ArrayHelpers.Grow(ref entityData.changes, entityData.changesCount << 1);
                }
                
                entityData.changes[entityData.changesCount++] = StructuralChange.Create(typeInfo.id, true);
            }
            
            entityData.nextArchetypeHash = entityData.nextArchetypeHash.Combine(typeInfo.hash);
            MLogger.LogTrace($"[AddComponent] To: {entityData.nextArchetypeHash}");
            
            world.dirtyEntities.Add(entity.Id);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void TransientChangeRemoveComponent(this World world, Entity entity, ref TypeInfo typeInfo) {
            ref var entityData = ref world.entities[entity.Id];
            
            if (!FilterDuplicateOperationType(ref entityData, typeInfo.id)) {
                if (entityData.changesCount == entityData.changes.Length) {
                    ArrayHelpers.Grow(ref entityData.changes, entityData.changesCount << 1);
                }
                
                entityData.changes[entityData.changesCount++] = StructuralChange.Create(typeInfo.id, false);
            }
            
            entityData.nextArchetypeHash = entityData.nextArchetypeHash.Combine(typeInfo.hash);
            MLogger.LogTrace($"[RemoveComponent] To: {entityData.nextArchetypeHash}");
            
            world.dirtyEntities.Add(entity.Id);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool FilterDuplicateOperationType(ref EntityData entityData, int typeId) {
            var changesCount = entityData.changesCount;
            
            for (var i = 0; i < changesCount; i++) {
                if (entityData.changes[i].typeId != typeId) {
                    continue;
                }
                
                entityData.changes[i] = entityData.changes[entityData.changesCount - 1];
                --entityData.changesCount;
                
                return true;
            }
            
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void ApplyTransientChanges(this World world, Entity entity, ref EntityData entityData) {
            // No changes
            if (entityData.changesCount == 0) {
                return;
            }
            
            // Add to new archetype
            if (!world.archetypes.TryGetValue(entityData.nextArchetypeHash.GetValue(), out var nextArchetype)) {
                nextArchetype = world.CreateMigratedArchetype(ref entityData);
            }
            
            var indexInNextArchetype = nextArchetype.Add(entity);
            
            // Remove from previous archetype
            if (entityData.currentArchetype != null) {
                var index = entityData.indexInCurrentArchetype;
                entityData.currentArchetype.Remove(index);
                
                var entityIndex = entityData.currentArchetype.entities[index].Id;
                world.entities[entityIndex].indexInCurrentArchetype = index;
                
                world.TryScheduleArchetypeForRemoval(entityData.currentArchetype);
            }
            
            // Finalize migration
            MLogger.LogTrace($"[WorldExtensions] Finalize migration for entity {entity} to archetype {nextArchetype.hash}");
            entityData.currentArchetype = nextArchetype;
            entityData.indexInCurrentArchetype = indexInNextArchetype;
            
            entityData.changesCount = 0;
            entityData.nextArchetypeHash = nextArchetype.hash;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void TryScheduleArchetypeForRemoval(this World world, Archetype archetype) {
            if (!archetype.IsEmpty()) {
                return;
            }
            
            MLogger.LogTrace($"[WorldExtensions] Schedule archetype {archetype.hash} for removal");
            world.emptyArchetypes.Add(archetype);
        }
        
        internal static Archetype CreateMigratedArchetype(this World world, ref EntityData entityData) {
            var nextArchetype = world.archetypePool.Rent(entityData.nextArchetypeHash);
            
            if (entityData.currentArchetype != null) {
                MLogger.LogTrace($"[WorldExtensions] Copy {entityData.currentArchetype.components.length} components from base archetype {entityData.currentArchetype.hash}");
                foreach (var typeId in entityData.currentArchetype.components) {
                    MLogger.LogTrace($"[WorldExtensions] Copy component {typeId} from base archetype {entityData.currentArchetype.hash}");
                    nextArchetype.components.Add(typeId);
                }
            } else {
                MLogger.LogTrace($"[WorldExtensions] Base archetype is null");
            }
            
            MLogger.LogTrace($"[WorldExtensions] Add {entityData.changesCount} components to archetype {entityData.nextArchetypeHash}");
            var changesCount = entityData.changesCount;
            for (var i = 0; i < changesCount; i++) {
                var structuralChange = entityData.changes[i];
                if (structuralChange.isAddition) {
                    MLogger.LogTrace($"[WorldExtensions] Add {structuralChange.typeId} to archetype {entityData.nextArchetypeHash}");
                    nextArchetype.components.Add(structuralChange.typeId);
                } else {
                    MLogger.LogTrace($"[WorldExtensions] Remove {structuralChange.typeId} from archetype {entityData.nextArchetypeHash}");
                    nextArchetype.components.Remove(structuralChange.typeId);
                }
            }

            if (entityData.currentArchetype != null) {
                AddMatchingPreviousFilters(nextArchetype, ref entityData);
            }
            
            AddMatchingDeltaFilters(world, nextArchetype, ref entityData);
            
            world.archetypes.Add(nextArchetype.hash.GetValue(), nextArchetype, out _);
            world.archetypesCount++;
            
            return nextArchetype;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void AddMatchingPreviousFilters(Archetype archetype, ref EntityData entityData) {
            var filters = entityData.currentArchetype.filters;
            
            foreach (var idx in filters) {
                var filter = filters.GetValueByIndex(idx);
                if (filter.AddArchetypeIfMatches(archetype)) {
                    MLogger.LogTrace($"[WorldExtensions] Add PREVIOUS {filter} to archetype {archetype.hash}");
                    archetype.AddFilter(filter);
                } else {
                    MLogger.LogTrace($"[WorldExtensions] PREVIOUS {filter} does not match archetype {archetype.hash}");
                }
            }
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void AddMatchingDeltaFilters(World world, Archetype archetype, ref EntityData transient) {
            var changesCount = transient.changesCount;
            for (var i = 0; i < changesCount; i++) {
                var structuralChange = transient.changes[i];
                
                var filters = world.componentsToFiltersRelation.GetFilters(structuralChange.typeId);
                if (filters == null) {
                    MLogger.LogTrace($"[WorldExtensions] No DELTA filters for component {structuralChange.typeId}");
                    continue;
                }
                
                MLogger.LogTrace($"[WorldExtensions] Found {filters.Length} DELTA filters for component {structuralChange.typeId}");
                
                var filtersCount = filters.Length;
                for (var j = 0; j < filtersCount; j++) {
                    var filter = filters[j];
                    
                    if (filter.AddArchetypeIfMatches(archetype)) {
                        MLogger.LogTrace($"[WorldExtensions] Add DELTA filter {filter} to archetype {archetype.hash}");
                        archetype.AddFilter(filter);
                    } else {
                        MLogger.LogTrace($"[WorldExtensions] DELTA filter {filter} does not match archetype {archetype.hash}");
                    }
                }
            }
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void CompleteEntityDisposal(this World world, int entityId, ref EntityData entityData) {
            if (entityData.currentArchetype != null) {
                var index = entityData.indexInCurrentArchetype;
                entityData.currentArchetype.Remove(index);
                
                var entityIndex = entityData.currentArchetype.entities[index].Id;
                world.entities[entityIndex].indexInCurrentArchetype = index;
                
                world.TryScheduleArchetypeForRemoval(entityData.currentArchetype);
                
                entityData.currentArchetype = null;
            }

            entityData.changesCount = 0;
            entityData.nextArchetypeHash = default;
            
            world.nextFreeEntityIDs.Push(entityId);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void IncrementGeneration(this World world, int entityId) {
            unchecked {
                world.entitiesGens[entityId]++;
            }
        }
        
        [PublicAPI]
        public static void WarmupArchetypes(this World world, int count) {
            world.ThreadSafetyCheck();
            
            world.archetypePool.WarmUp(count);
        }

        [PublicAPI]
        public static void SetThreadId(this World world, int threadId) {
            world.ThreadSafetyCheck();
            world.threadIdLock = threadId;
        }
        
        [PublicAPI]
        public static int GetThreadId(this World world) {
            return world.threadIdLock;
        }

        [System.Diagnostics.Conditional("MORPEH_THREAD_SAFETY")]
        internal static void ThreadSafetyCheck(this World world) {
            if (world == null) {
                return;
            }

            var currentThread = Environment.CurrentManagedThreadId;
            if (world.threadIdLock != currentThread) {
                throw new Exception($"[MORPEH] Thread safety check failed. You are trying touch the world from a thread {currentThread}, but the world associated with the thread {world.threadIdLock}");
            }
        }
        
        [PublicAPI]
        public static AspectFactory<T> GetAspectFactory<T>(this World world) where T : struct, IAspect {
            world.ThreadSafetyCheck();
            var aspectFactory = default(AspectFactory<T>);
            aspectFactory.value.OnGetAspectFactory(world);
            return aspectFactory;
        }
        
        [PublicAPI]
        public static bool IsNullOrDisposed(this World world) {
            return world == null || world.IsDisposed;
        }
    }
}
