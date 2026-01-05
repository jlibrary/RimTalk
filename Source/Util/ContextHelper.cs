using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using static RimTalk.Service.PromptService;

namespace RimTalk.Util;

public enum NearbyKind
{
    Building,
    Item,
    Plant,
    Animal,
    Filth
}

public struct NearbyAgg
{
    public NearbyKind Kind;
    public string Key;        // Stable aggregation key (do NOT use dynamic labels)
    public string Label;      // Display label (human readable)
    public int Count;         // Number of instances aggregated
    public int StackSum;      // For items: sum of stackCount across instances
}

public static class ContextHelper
{
    public static string GetPawnLocationStatus(Pawn pawn)
    {
        if (pawn?.Map == null || pawn.Position == IntVec3.Invalid)
            return null;

        var room = pawn.GetRoom();
        return room is { PsychologicallyOutdoors: false }
            ? "Indoors".Translate()
            : "Outdoors".Translate();
    }

    public static Dictionary<Thought, float> GetThoughts(Pawn pawn)
    {
        var thoughts = new List<Thought>();
        pawn?.needs?.mood?.thoughts?.GetAllMoodThoughts(thoughts);

        return thoughts
            .GroupBy(t => t.def.defName)
            .ToDictionary(g => g.First(), g => g.Sum(t => t.MoodOffset()));
    }

    public static string GetDecoratedName(Pawn pawn)
    {
        if (!pawn.RaceProps.Humanlike)
            return $"{pawn.LabelShort}({pawn.ageTracker.AgeBiologicalYears}/{pawn.def.LabelCap})";

        var race = ModsConfig.BiotechActive && pawn.genes?.Xenotype != null
            ? pawn.genes.XenotypeLabel
            : pawn.def.LabelCap.RawText;

        return $"{pawn.LabelShort}({pawn.ageTracker.AgeBiologicalYears}{pawn.gender.GetLabelShort()}/{pawn.GetRole(true)}/{race})";
    }

    public static bool IsWall(Thing thing)
    {
        var data = thing.def.graphicData;
        return data != null && data.linkFlags.HasFlag((Enum)LinkFlags.Wall);
    }

    public static string Sanitize(string text, Pawn pawn = null)
    {
        if (pawn != null)
            text = text.Formatted(pawn.Named("PAWN")).AdjustedFor(pawn).Resolve();
        return text.StripTags().RemoveLineBreaks();
    }

    public static string FormatBackstory(string label, BackstoryDef backstory, Pawn pawn, InfoLevel infoLevel)
    {
        var result = $"{label}: {backstory.title}({backstory.titleShort})";
        if (infoLevel == InfoLevel.Full)
            result += $":{Sanitize(backstory.description, pawn)}";
        return result;
    }

    public static List<IntVec3> GetNearbyCells(Pawn pawn, int distance = 5)
    {
        var cells = new List<IntVec3>();
        var facing = pawn.Rotation.FacingCell;

        for (int i = 1; i <= distance; i++)
        {
            var targetCell = pawn.Position + facing * i;
            for (int offset = -1; offset <= 1; offset++)
            {
                var cell = new IntVec3(targetCell.x + offset, targetCell.y, targetCell.z);
                if (cell.InBounds(pawn.Map))
                    cells.Add(cell);
            }
        }

        return cells;
    }

    private static List<IntVec3> GetNearbyCellsRadial(Pawn pawn, int radius, bool sameRoomOnly)
    {
        var map = pawn.Map;
        var origin = pawn.Position;

        Room room = null;
        if (sameRoomOnly)
            room = origin.GetRoom(map);

        var cells = new List<IntVec3>(128);

        // GenRadial is the standard RimWorld radial cell enumeration
        foreach (var c in GenRadial.RadialCellsAround(origin, radius, true))
        {
            if (!c.InBounds(map)) continue;

            if (sameRoomOnly && room != null)
            {
                var r2 = c.GetRoom(map);
                if (r2 != room) continue;
            }

            cells.Add(c);
        }

        return cells;
    }


    /// <summary>
    /// RimWorld 1.6 "hidden" is handled by HiddenItemsManager (player discovery / codex),
    /// not by a ThingComp (CompHiddenable no longer exists in 1.6).
    /// </summary>
    public static bool IsHiddenForPlayer(Thing thing)
    {
        if (thing?.def == null) return false;
        if (Find.HiddenItemsManager == null) return false;
        return Find.HiddenItemsManager.Hidden(thing.def);
    }

    /// <summary>
    /// Collect structured nearby context with strong limits to avoid freezing on storage/compression mods.
    /// The limits are conservative by design; tune if needed.
    /// </summary>
    public static List<NearbyAgg> CollectNearbyContext(
        Pawn pawn,
        int distance = 5,
        int maxPerKind = 12,
        int maxCellsToScan = 18,
        int maxThingsTotal = 200,
        int maxItemThings = 120)
    {
        if (pawn?.Map == null || pawn.Position == IntVec3.Invalid)
            return new List<NearbyAgg>();

        var map = pawn.Map;

        // Limit the number of cells to scan (hard cap).
        var sameRoomOnly = pawn.GetRoom() is { PsychologicallyOutdoors: false };
        var cells = GetNearbyCellsRadial(pawn, distance, sameRoomOnly);
        if (cells.Count > maxCellsToScan)
            cells = cells.Take(maxCellsToScan).ToList();

        var aggs = new Dictionary<string, NearbyAgg>();
        var seenBuildingIds = new HashSet<int>();

        int processedTotal = 0;
        int processedItems = 0;

        foreach (var cell in cells)
        {
            var thingsHere = cell.GetThingList(map);
            if (thingsHere == null || thingsHere.Count == 0)
                continue;

            // Use index loop to reduce foreach iterator overhead under heavy lists.
            for (int i = 0; i < thingsHere.Count; i++)
            {
                if (processedTotal >= maxThingsTotal)
                    goto DONE;

                var thing = thingsHere[i];
                if (thing?.def == null) continue;
                if (thing.DestroyedOrNull()) continue;

                // Skip hidden defs (player undiscovered / codex-hidden).
                if (Find.HiddenItemsManager != null && Find.HiddenItemsManager.Hidden(thing.def))
                    continue;

                // Hard cap on number of item-things processed. This is the main safeguard against
                // storage/compression mods that can expose extremely large item lists or expensive enumeration.
                if (thing.def.category == ThingCategory.Item)
                {
                    // Skip items in storage/stockpiles/shelves to avoid huge enumerations
                    // and compatibility issues with compression storage mods.
                    if (thing.Position.GetSlotGroup(map) != null)
                        continue;

                    processedItems++;
                    if (processedItems > maxItemThings)
                        goto DONE;

                    if (thing.stackCount >= 1000 && thing.def.stackLimit < 1000)
                        continue;
                }

                processedTotal++;

                // Animals (Pawn) are handled separately.
                if (thing is Pawn otherPawn)
                {
                    if (otherPawn == pawn) continue;
                    if (!otherPawn.Spawned || otherPawn.Dead) continue;
                    if (!otherPawn.RaceProps.Animal) continue;
                    AddAgg(aggs, otherPawn, NearbyKind.Animal);
                    continue;
                }

                var cat = thing.def.category;

                if (cat == ThingCategory.Building)
                {
                    if (!seenBuildingIds.Add(thing.thingIDNumber)) continue;
                    if (IsWall(thing)) continue;
                    AddAgg(aggs, thing, NearbyKind.Building);
                }
                else if (cat == ThingCategory.Item)
                {
                    AddAgg(aggs, thing, NearbyKind.Item);
                }
                else if (cat == ThingCategory.Plant)
                {
                    AddAgg(aggs, thing, NearbyKind.Plant);
                }
                else if (thing.def.IsFilth)
                {
                    AddAgg(aggs, thing, NearbyKind.Filth);
                }
            }
        }

DONE:
        // Output compression: take top N per kind by instance count.
        return aggs.Values
            .GroupBy(a => a.Kind)
            .SelectMany(g => g
                .OrderByDescending(x => x.Count)
                .Take(maxPerKind))
            .ToList();
    }

    /// <summary>
    /// Add/update aggregation entry.
    /// IMPORTANT: aggregation key must be stable; do NOT use Thing.LabelCap/LabelNoCount as key,
    /// because many items (books/art) have dynamic labels (title/quality/hp) and will not dedupe.
    /// </summary>
    private static void AddAgg(Dictionary<string, NearbyAgg> aggs, Thing thing, NearbyKind kind)
    {
        var def = thing.def;

        // Stable display label: for context, prefer def.LabelCap instead of Thing.LabelCap
        // to avoid embedding dynamic info like book titles, author names, quality, hitpoints, etc.
        var label = def.LabelCap;

        // Stable key: kind + defName (optionally add stuff if you want to distinguish materials).
        var key = $"{kind}|{def.defName}";

        if (!aggs.TryGetValue(key, out var agg))
        {
            agg = new NearbyAgg
            {
                Kind = kind,
                Key = key,
                Label = label,
                Count = 0,
                StackSum = 0
            };
        }

        agg.Count++;

        if (kind == NearbyKind.Item)
            agg.StackSum += thing.stackCount;

        aggs[key] = agg;
    }

    public static string FormatNearbyContext(List<NearbyAgg> aggs)
    {
        if (aggs == null || aggs.Count == 0)
            return null;

        string FmtGroup(NearbyKind kind, string title)
        {
            var list = aggs.Where(a => a.Kind == kind).ToList();
            if (list.Count == 0) return null;

            var parts = list.Select(a =>
            {
                if (kind == NearbyKind.Item)
                {
                    // Reduce noise: only show "(N stacks)" when N > 1.
                    if (a.Count > 1)
                        return $"{a.Label} ×{a.StackSum} ({a.Count} stacks)";
                    return $"{a.Label} ×{a.StackSum}";
                }

                return a.Count > 1 ? $"{a.Label} ×{a.Count}" : a.Label;
            });

            return $"{title}: {string.Join(", ", parts)}";
        }

        var sections = new List<string>
        {
            FmtGroup(NearbyKind.Building, "Buildings"),
            FmtGroup(NearbyKind.Item, "Items"),
            FmtGroup(NearbyKind.Plant, "Plants"),
            FmtGroup(NearbyKind.Animal, "Animals"),
            FmtGroup(NearbyKind.Filth, "Filth"),
        }.Where(s => !string.IsNullOrWhiteSpace(s));

        return string.Join("\n", sections);
    }

    public static string CollectNearbyContextText(
        Pawn pawn,
        int distance = 5,
        int maxPerKind = 12,
        int maxCellsToScan = 18,
        int maxThingsTotal = 200,
        int maxItemThings = 120)
    {
        var aggs = CollectNearbyContext(pawn, distance, maxPerKind, maxCellsToScan, maxThingsTotal, maxItemThings);
        return FormatNearbyContext(aggs);
    }
}