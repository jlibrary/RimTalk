using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimLife;
using RimTalk.Source.Data;
using RimWorld;
using Verse;

//namespace RimTalk.Service
//{
// // Simple adapter that builds an AI system context using RimLife.PawnPro output.
// // For testing: use Full output for the initiator and Lite for other participants.
// public static class PawnProContextService
// {
// public static string BuildContextFromPawnPro(List<Pawn> pawns, Pawn initiator)
// {
// if (pawns == null || pawns.Count ==0) return string.Empty;

// var sb = new StringBuilder();

// // Preserve original instruction/system prompt so LLM knows how to behave.
// try
// {
// sb.Append(Constant.Instruction).Append('\n');
// }
// catch
// {
// // If Constant isn't available for some reason, ignore.
// }

// int idx =1;
// foreach (var p in pawns)
// {
// if (p == null) continue;
// sb.Append($"[Person {idx} START]\n");
// try
// {
// var pro = new RimLife.PawnPro(p);
// if (initiator != null && p == initiator)
// {
// sb.Append(pro.ToStringFull());
// }
// else
// {
// sb.Append(pro.ToStringLite());
// }
// }
// catch
// {
// // fallback to minimal identification
// sb.Append(p?.LabelShort ?? "Unknown");
// }

// sb.Append('\n').Append($"[Person {idx} END]\n");
// idx++;
// }

// return sb.ToString();
// }
// }
//}
