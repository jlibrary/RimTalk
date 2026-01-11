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
        foreach (var pawn in PawnCache.Keys.ToList())
        {
            if (!pawn.IsTalkEligible())
            {
                if (PawnCache.TryRemove(pawn, out var removedState))
                {
                    NameCache.TryRemove(removedState.Pawn.LabelShort, out _); 
                }
                continue;
            }

            // If eligible, ensure the NameCache points to the CURRENT name.
            var label = pawn.LabelShort;
            if (!string.IsNullOrEmpty(label))
            {
                NameCache[label] = pawn;
            }
        }

        // Ensure player state/name is valid.
        InitializePlayerPawn();

        // Remove "Ghost Keys" (old names).
        foreach (var entry in NameCache.ToArray())
        {
            var pawn = entry.Value;
            if (pawn == null || !PawnCache.ContainsKey(pawn) || pawn.LabelShort != entry.Key)
            {
                NameCache.TryRemove(entry.Key, out _);
            }
        }

        // Add new eligible pawns to all caches.
        foreach (Pawn pawn in Find.CurrentMap.mapPawns.AllPawnsSpawned)
        {
            if (pawn.IsTalkEligible() && !PawnCache.ContainsKey(pawn))
            {
                PawnCache[pawn] = new PawnState(pawn);
                NameCache[pawn.LabelShort] = pawn;
            }
        }
    }

    public static IEnumerable<PawnState> GetAll()
    {
        return PawnCache.Values;
    }

    public static void Clear()
    {
        PawnCache.Clear();
        NameCache.Clear();
        _playerPawn = null;
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

        // 1. Calculate Group Weights
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

        if (baselineWeight <= 0) return null;

        // 2. Determine scaling factors
        var colonistScaleFactor = GetScaleFactor(totalColonistWeight, baselineWeight);
        var visitorScaleFactor = GetScaleFactor(totalVisitorWeight, baselineWeight);
        var enemyScaleFactor = GetScaleFactor(totalEnemyWeight, baselineWeight);
        var slaveScaleFactor = GetScaleFactor(totalSlaveWeight, baselineWeight);
        var prisonerScaleFactor = GetScaleFactor(totalPrisonerWeight, baselineWeight);

        // 3. Calculate effective total weight
        double effectiveTotalWeight = 
            totalColonistWeight * colonistScaleFactor +
            totalVisitorWeight * visitorScaleFactor +
            totalEnemyWeight * enemyScaleFactor +
            totalSlaveWeight * slaveScaleFactor +
            totalPrisonerWeight * prisonerScaleFactor;

        if (effectiveTotalWeight <= 0) return null;

        // 4. Absolute Probability Check
        // If the total weight of the colony is low (e.g. everyone sleeping or shy),
        // we might return null to simulate silence.
        if (effectiveTotalWeight < 1.0 && Random.NextDouble() > effectiveTotalWeight)
        {
            return null;
        }

        // 5. Select Pawn
        var randomWeight = Random.NextDouble() * effectiveTotalWeight;
        var cumulativeWeight = 0.0;

        foreach (var pawn in pawnList)
        {
            var currentPawnWeight = Get(pawn)?.TalkInitiationWeight ?? 0.0;
            double currentEffectiveWeight = 0.0;

            if (pawn.IsFreeNonSlaveColonist || pawn.HasVocalLink()) currentEffectiveWeight = currentPawnWeight * colonistScaleFactor;
            else if (pawn.IsSlave) currentEffectiveWeight = currentPawnWeight * slaveScaleFactor;
            else if (pawn.IsPrisoner) currentEffectiveWeight = currentPawnWeight * prisonerScaleFactor;
            else if (pawn.IsVisitor()) currentEffectiveWeight = currentPawnWeight * visitorScaleFactor;
            else if (pawn.IsEnemy()) currentEffectiveWeight = currentPawnWeight * enemyScaleFactor;
            
            cumulativeWeight += currentEffectiveWeight;

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