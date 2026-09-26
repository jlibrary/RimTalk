using System;
using System.Linq;
using System.Text;
using HarmonyLib;
using RimTalk.Data;
using RimTalk.Source.Data;
using RimWorld;
using Verse;

namespace RimTalk.Patch;

[StaticConstructorOnStartup]
[HarmonyPatch(typeof(Archive), nameof(Archive.Add))]
public static class ArchivePatch
{
    static ArchivePatch()
    {
        Settings.MigrateArchivableSettings();
    }

    public static void Prefix(IArchivable archivable)
    {
        ProcessArchivable(archivable);
    }

    public static void ProcessArchivable(IArchivable archivable)
    {
        if (!ShouldProcessArchivable(archivable))
        {
            return;
        }

        // Generate the prompt text first, as it's needed in all cases.
        // Decide quest category & generate prompt (kept compatible with original text)
        var (prompt, talkType) = GeneratePrompt(archivable);
        var eventMap = FindLocation(archivable);

        TalkRequestPool.Add(prompt, mapId: eventMap?.uniqueID ?? -1, talkType: talkType);
    }

    public static void SyncActiveLetters()
    {
        var letterStack = Find.LetterStack;
        if (letterStack?.LettersListForReading == null) return;

        int currentTick = GenTicks.TicksGame;
        var activeRequests = TalkRequestPool.GetAllActive().ToList();

        foreach (var letter in letterStack.LettersListForReading)
        {
            if (letter == null) continue;
            int arrivalTick = letter.arrivalTick > 0 ? letter.arrivalTick : currentTick;
            if (currentTick - arrivalTick > 5000) continue;

            var (prompt, _) = GeneratePrompt(letter);
            if (activeRequests.Any(r => r.Prompt == prompt)) continue;

            ProcessArchivable(letter);
        }
    }

    private static bool ShouldProcessArchivable(IArchivable archivable)
    {
        var settings = Settings.Get();
        var enabledTypes = settings.EnabledArchivableTypes;

        // Messages are disabled by default unless explicitly enabled
        if (archivable is Message message)
        {
            if (!enabledTypes.TryGetValue("Verse.Message", out var isTypeEnabled) || !isTypeEnabled)
                return false;

            if (message.def != null)
            {
                if (enabledTypes.TryGetValue("Verse.Message:" + message.def.defName, out var isMsgDefEnabled))
                    return isMsgDefEnabled;
                if (enabledTypes.TryGetValue(message.def.defName, out var isDefEnabled))
                    return isDefEnabled;
            }

            return true;
        }

        // Letters and other events are enabled by default unless explicitly disabled
        string typeName = archivable.GetType().FullName;
        if (enabledTypes.TryGetValue(typeName, out var isEnabled) && !isEnabled)
        {
            return false;
        }

        if (archivable is Letter letter && letter.def != null)
        {
            if (enabledTypes.TryGetValue(letter.def.defName, out var isDefEnabled))
            {
                if (!isDefEnabled && IsSharedMessageDef(letter.def.defName))
                {
                    return isEnabled;
                }
                return isDefEnabled;
            }
        }

        return true;
    }

    private static bool IsSharedMessageDef(string defName)
    {
        return DefDatabase<MessageTypeDef>.GetNamedSilentFail(defName) != null;
    }


    private static (string prompt, TalkType talkType) GeneratePrompt(IArchivable archivable)
    {
        var talkType = TalkType.Event;
        string prompt;
        string targetSuffix = GetTargetSuffix(archivable);
        string label = archivable?.ArchivedLabel?.StripTags()?.Trim();

        if (archivable is ChoiceLetter { quest: not null } choiceLetter)
        {
            if (choiceLetter.quest.State == QuestState.NotYetAccepted)
            {
                talkType = TalkType.QuestOffer;
                string desc = TruncateEventDescription(choiceLetter.quest.description.ToString());
                prompt = !string.IsNullOrWhiteSpace(label)
                    ? $"(Talk if you want to accept quest: {label})\n[{desc}]"
                    : $"(Talk if you want to accept quest)\n[{desc}]";
            }
            else
            {
                talkType = TalkType.QuestEnd;
                string desc = TruncateEventDescription(archivable.ArchivedTooltip);
                prompt = !string.IsNullOrWhiteSpace(label)
                    ? $"(Talk about quest result: {label})\n[{desc}]"
                    : $"(Talk about quest result)\n[{desc}]";
            }
        }
        else
        {
            var tip = archivable?.ArchivedTooltip ?? string.Empty;
            if (ContainsQuestReference(label ?? string.Empty, tip))
            {
                talkType = TalkType.QuestEnd;
                string desc = TruncateEventDescription(tip);
                prompt = !string.IsNullOrWhiteSpace(label)
                    ? $"(Talk about quest result: {label})\n[{desc}]"
                    : $"(Talk about quest result)\n[{desc}]";
            }
            else
            {
                string desc = TruncateEventDescription(tip);
                prompt = !string.IsNullOrWhiteSpace(label)
                    ? $"(Talk about incident: {label})\n[{desc}{targetSuffix}]"
                    : $"(Talk about incident)\n[{desc}{targetSuffix}]";
            }
        }

        return (prompt, talkType);
    }

    private static string TruncateEventDescription(string text, int maxLength = 140)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var cleaned = text.StripTags().Trim();
        if (cleaned.Length <= maxLength) return cleaned;

        var paragraphs = cleaned.Split(new[] { "\r\n\r\n", "\n\n", "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        if (paragraphs.Length > 1)
        {
            var sb = new StringBuilder();
            foreach (var p in paragraphs)
            {
                var trimmed = p.Trim();
                if (trimmed.Length == 0) continue;

                if (sb.Length + trimmed.Length + 1 > maxLength)
                {
                    if (sb.Length == 0)
                    {
                        return TruncateToSentenceOrLength(trimmed, maxLength);
                    }
                    break;
                }

                if (sb.Length > 0) sb.Append("\n");
                sb.Append(trimmed);
            }

            if (sb.Length > 0)
                return sb.ToString();
        }

        return TruncateToSentenceOrLength(cleaned, maxLength);
    }

    private static string TruncateToSentenceOrLength(string text, int maxLength)
    {
        if (text.Length <= maxLength) return text;
        int maxScan = Math.Min(maxLength + 20, text.Length);
        for (int i = maxScan - 1; i >= 30; i--)
        {
            char c = text[i];
            if (c == '.' || c == '!' || c == '?')
            {
                return text.Substring(0, i + 1).Trim();
            }
        }
        return text.Substring(0, maxLength).Trim() + "...";
    }

    private static string GetTargetSuffix(IArchivable archivable)
    {
        var pawn = archivable?.LookTargets?.PrimaryTarget.Thing as Pawn
            ?? archivable?.LookTargets?.targets?.Select(t => t.Thing as Pawn).FirstOrDefault(p => p != null);

        return pawn != null ? $" (Target: {pawn.LabelShort})" : string.Empty;
    }


    private static bool ContainsQuestReference(string label, string tip)
    {
        return label.IndexOf("Quest", StringComparison.OrdinalIgnoreCase) >= 0
            || tip.IndexOf("Quest", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static Map FindLocation(IArchivable archivable)
    {
        if (archivable.LookTargets is not { Any: true })
            return null;

        return archivable.LookTargets.PrimaryTarget.Map 
            ?? archivable.LookTargets.targets.Select(t => t.Map).FirstOrDefault(m => m != null);
    }
}
