using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimTalk.Data;
using RimTalk.Service;
using RimTalk.Source.Data;
using RimTalk.Util;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimTalk.Prompt;

/// <summary>
/// Helper methods for extracting context information from pawns for Mustache templates.
/// These methods are used by MustacheParser to provide data for template variables.
/// </summary>
public static class MustacheContextProvider
{
    /// <summary>
    /// Generates a description string of the dialogue type.
    /// Used by {{dialogue.type}} variable.
    /// Delegates to ContextBuilder.BuildDialogueType() to avoid code duplication.
    /// </summary>
    public static string GetDialogueTypeString(TalkRequest talkRequest, List<Pawn> pawns)
    {
        if (pawns == null || pawns.Count == 0) return "";
        
        var mainPawn = pawns[0];
        var shortName = mainPawn.LabelShort;
        var sb = new StringBuilder();
        
        // Delegate to ContextBuilder to avoid duplicating dialogue type logic
        ContextBuilder.BuildDialogueType(sb, talkRequest, pawns, shortName, mainPawn);
        
        return sb.ToString();
    }

    /// <summary>
    /// Gets location string for a pawn.
    /// Used by {{environment.location}} variable.
    /// </summary>
    public static string GetLocationString(Pawn pawn)
    {
        if (pawn?.Map == null) return "";
        
        var locationStatus = ContextHelper.GetPawnLocationStatus(pawn);
        if (string.IsNullOrEmpty(locationStatus)) return "";
        
        var temperature = Mathf.RoundToInt(pawn.Position.GetTemperature(pawn.Map));
        var room = pawn.GetRoom();
        var roomRole = room is { PsychologicallyOutdoors: false } ? room.Role?.label ?? "" : "";

        return string.IsNullOrEmpty(roomRole)
            ? $"{locationStatus};{temperature}C"
            : $"{locationStatus};{temperature}C;{roomRole}";
    }

    /// <summary>
    /// Gets beauty string for a pawn's location.
    /// Used by {{environment.beauty}} variable.
    /// </summary>
    public static string GetBeautyString(Pawn pawn)
    {
        if (pawn?.Map == null) return "";
        
        var nearbyCells = ContextHelper.GetNearbyCells(pawn);
        if (nearbyCells.Count == 0) return "";
        
        var beautySum = nearbyCells.Sum(c => BeautyUtility.CellBeauty(c, pawn.Map));
        return Describer.Beauty(beautySum / nearbyCells.Count);
    }

    /// <summary>
    /// Gets cleanliness string for a pawn's room.
    /// Used by {{environment.cleanliness}} variable.
    /// </summary>
    public static string GetCleanlinessString(Pawn pawn)
    {
        if (pawn?.Map == null) return "";
        
        var room = pawn.GetRoom();
        if (room is not { PsychologicallyOutdoors: false }) return "";
        
        return Describer.Cleanliness(room.GetStat(RoomStatDefOf.Cleanliness));
    }
}