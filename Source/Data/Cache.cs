using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using RimTalk.Util;
using RimWorld;
using Verse;
using Random = System.Random;

namespace RimTalk.Data;

public static class Cache
{
    // Main data store mapping a Pawn to its current state.
    private static readonly ConcurrentDictionary<Pawn, PawnState> PawnCache = new();

    private static readonly ConcurrentDictionary<string, Pawn> NameCache = new();

    // Spatial index: map -> (chunkCoord -> set of pawns in that chunk)
    private const int ChunkSize = 8; // cells per chunk (tunable)
    private static readonly ConcurrentDictionary<Map, ConcurrentDictionary<(int x, int z), ConcurrentDictionary<Pawn, byte>>> MapChunkIndex = new();

    // This Random instance is still needed for the weighted selection method.
    private static readonly Random Random = new();

    public static IEnumerable<Pawn> Keys => PawnCache.Keys;
    public static Pawn GetPlayer() => _playerPawn;

    // Invisible player pawn
    private static Pawn _playerPawn;

    public static PawnState Get(Pawn pawn)
    {
        if (pawn == null) return null;

        if (PawnCache.TryGetValue(pawn, out var state)) return state;

        if (!pawn.IsTalkEligible()) return null;
        
        PawnCache[pawn] = new PawnState(pawn);
        NameCache[pawn.LabelShort] = pawn;
        // add to spatial index if possible
        TryIndexPawn(pawn);
        return PawnCache[pawn];
    }

    /// <summary>
    /// Gets a pawn's state using a fast dictionary lookup by name.
    /// </summary>
    public static PawnState GetByName(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        return NameCache.TryGetValue(name, out var pawn) ? Get(pawn) : null;
    }

    public static void Refresh()
    {
        // Identify and remove ineligible pawns from all caches.
        foreach (var pawn in PawnCache.Keys.ToList().Where(pawn => !pawn.IsTalkEligible()))
        {
            if (PawnCache.TryRemove(pawn, out var removedState))
            {
                NameCache.TryRemove(removedState.Pawn.LabelShort, out _);
                RemoveFromIndex(pawn);
            }
        }

        // Add new eligible pawns to all caches.
        // Rebuild index for current map then ensure PawnCache contains spawned pawns
        var currentMap = Find.CurrentMap;
        if (currentMap != null)
        {
            // clear existing index for this map
            MapChunkIndex.TryRemove(currentMap, out _);

            foreach (Pawn pawn in currentMap.mapPawns.AllPawnsSpawned)
            {
                if (pawn.IsTalkEligible() && !PawnCache.ContainsKey(pawn))
                {
                    PawnCache[pawn] = new PawnState(pawn);
                    NameCache[pawn.LabelShort] = pawn;
                }

                if (pawn.IsTalkEligible())
                {
                    TryIndexPawn(pawn);
                }
            }
        }

        if (_playerPawn == null)
            InitializePlayerPawn();
    }

    public static IEnumerable<PawnState> GetAll()
    {
        return PawnCache.Values;
    }

    /// <summary>
    /// Returns pawns near a cell using the spatial chunk index. Falls back to scanning all cached pawns if map/index unavailable.
    /// </summary>
    public static IEnumerable<Pawn> GetPawnsNear(Map map, IntVec3 cell, int radiusCells)
    {
        if (MapChunkIndex.TryGetValue(map, out var chunkDict))
        {
            var cellChunkX = cell.x / ChunkSize;
            var cellChunkZ = cell.z / ChunkSize;

            int chunkRadius = (radiusCells / ChunkSize) + 1;

            var collected = new HashSet<Pawn>();

            for (int dx = -chunkRadius; dx <= chunkRadius; dx++)
            for (int dz = -chunkRadius; dz <= chunkRadius; dz++)
            {
                var key = (cellChunkX + dx, cellChunkZ + dz);
                if (chunkDict.TryGetValue(key, out var set))
                {
                    foreach (var pawn in set.Keys)
                    {
                        if (pawn != null && pawn.Spawned)
                        {
                            // final filter by exact cell distance
                            var pos = pawn.Position;
                            if (pos.x >= cell.x - radiusCells && pos.x <= cell.x + radiusCells && pos.z >= cell.z - radiusCells && pos.z <= cell.z + radiusCells)
                                collected.Add(pawn);
                        }
                    }
                }
            }

            return collected;
        }

        // fallback: scan all cached pawns on same map
        return PawnCache.Keys.Where(p => p.Position.x >= cell.x - radiusCells && p.Position.x <= cell.x + radiusCells && p.Position.z >= cell.z - radiusCells && p.Position.z <= cell.z + radiusCells);
    }

    public static void Clear()
    {
        PawnCache.Clear();
        NameCache.Clear();
        _playerPawn = null;
        MapChunkIndex.Clear();
    }

    private static (int x, int z) CellToChunk(IntVec3 cell) => (cell.x / ChunkSize, cell.z / ChunkSize);

    private static void TryIndexPawn(Pawn pawn)
    {
        if (pawn == null || pawn.Map == null) return;
        var map = pawn.Map;
        var dict = MapChunkIndex.GetOrAdd(map, _ => new ConcurrentDictionary<(int x, int z), ConcurrentDictionary<Pawn, byte>>());
        var key = CellToChunk(pawn.Position);
        var set = dict.GetOrAdd(key, _ => new ConcurrentDictionary<Pawn, byte>());
        set[pawn] = 0;
    }

    private static void RemoveFromIndex(Pawn pawn)
    {
        if (pawn == null || pawn.Map == null) return;
        if (!MapChunkIndex.TryGetValue(pawn.Map, out var dict)) return;
        var key = CellToChunk(pawn.Position);
        if (!dict.TryGetValue(key, out var set)) return;
        set.TryRemove(pawn, out _);
    }

    private static double GetScaleFactor(double groupWeight, double baselineWeight)
    {
        if (baselineWeight <= 0 || groupWeight <= 0) return 0.0;
        if (groupWeight > baselineWeight) return baselineWeight / groupWeight;
        return 1.0;
    }

    /// <summary>
    /// Selects a random pawn from the provided list, with selection chance proportional to their TalkInitiationWeight.
    /// </summary>
    /// <param name="pawns">The collection of pawns to select from.</param>
    /// <returns>A single pawn, or null if the list is empty or no pawn has a weight > 0.</returns>
    public static Pawn GetRandomWeightedPawn(IEnumerable<Pawn> pawns)
    {
        var pawnList = pawns.ToList();
        if (pawnList.NullOrEmpty())
        {
            return null;
        }

        // 1. Categorize pawns and calculate the total weight for each group.
        double totalColonistWeight = 0.0;
        double totalVisitorWeight = 0.0;
        double totalEnemyWeight = 0.0;
        double totalSlaveWeight = 0.0;
        double totalPrisonerWeight = 0.0;

        foreach (var p in pawnList)
        {
            var weight = Get(p)?.TalkInitiationWeight ?? 0.0;
            if (p.IsFreeNonSlaveColonist || p.HasVocalLink()) totalColonistWeight += weight;
            else if (p.IsSlave) totalSlaveWeight += weight;
            else if (p.IsPrisoner) totalPrisonerWeight += weight;
            else if (p.IsVisitor()) totalVisitorWeight += weight;
            else if (p.IsEnemy()) totalEnemyWeight += weight;
        }

        // Use the colonist group weight as baseline. If it's zero, fall back to the heaviest group.
        double baselineWeight;
        if (totalColonistWeight > 0)
        {
            baselineWeight = totalColonistWeight;
        }
        else
        {
            baselineWeight = new[]
            {
                totalVisitorWeight,
                totalEnemyWeight,
                totalSlaveWeight,
                totalPrisonerWeight
            }.Max();
        }

        // If no one has any weight, no one can talk
        if (baselineWeight <= 0)
        {
            return null;
        }

        // 2. Determine scaling factors - groups above baseline get scaled down
        var colonistScaleFactor = GetScaleFactor(totalColonistWeight, baselineWeight);
        var visitorScaleFactor = GetScaleFactor(totalVisitorWeight, baselineWeight);
        var enemyScaleFactor = GetScaleFactor(totalEnemyWeight, baselineWeight);
        var slaveScaleFactor = GetScaleFactor(totalSlaveWeight, baselineWeight);
        var prisonerScaleFactor = GetScaleFactor(totalPrisonerWeight, baselineWeight);

        // 3. Calculate effective total weight
        var effectiveTotalWeight = pawnList.Sum(p =>
        {
            var weight = Get(p)?.TalkInitiationWeight ?? 0.0;
            if (p.IsFreeNonSlaveColonist || p.HasVocalLink()) return weight * colonistScaleFactor;
            if (p.IsSlave) return weight * slaveScaleFactor;
            if (p.IsPrisoner) return weight * prisonerScaleFactor;
            if (p.IsVisitor()) return weight * visitorScaleFactor;
            if (p.IsEnemy()) return weight * enemyScaleFactor;
            return 0;
        });

        var randomWeight = Random.NextDouble() * effectiveTotalWeight;
        var cumulativeWeight = 0.0;

        foreach (var pawn in pawnList)
        {
            var currentPawnWeight = Get(pawn)?.TalkInitiationWeight ?? 0.0;

            if (pawn.IsFreeNonSlaveColonist || pawn.HasVocalLink()) cumulativeWeight += currentPawnWeight * colonistScaleFactor;
            else if (pawn.IsSlave) cumulativeWeight += currentPawnWeight * slaveScaleFactor;
            else if (pawn.IsPrisoner) cumulativeWeight += currentPawnWeight * prisonerScaleFactor;
            else if (pawn.IsVisitor()) cumulativeWeight += currentPawnWeight * visitorScaleFactor;
            else if (pawn.IsEnemy()) cumulativeWeight += currentPawnWeight * enemyScaleFactor;

            if (randomWeight < cumulativeWeight)
            {
                return pawn;
            }
        }

        return pawnList.LastOrDefault(p => (Get(p)?.TalkInitiationWeight ?? 0.0) > 0);
    }

    public static void InitializePlayerPawn()
    {
        if (Current.Game == null || Settings.Get().PlayerName == _playerPawn?.Name.ToStringShort) return;
        
        _playerPawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist);
        _playerPawn.Name = new NameSingle(Settings.Get().PlayerName);
        PawnCache[_playerPawn] = new PawnState(_playerPawn);
        NameCache[_playerPawn.LabelShort] = _playerPawn;
    }
}