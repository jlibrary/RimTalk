using System.Collections.Generic;

namespace RimTalk.Prompt;

/// <summary>
/// Merges consecutive messages with the same role into a single message.
/// Preserves strict role alternation required by APIs like Gemini.
/// </summary>
internal static class PromptMessageRoleMerger
{
    internal static List<(PromptRole role, string content)> MergeConsecutiveRoles(
        List<(PromptRole role, string content)> messages,
        int mergeBoundary = -1)
    {
        if (messages == null || messages.Count <= 1)
            return messages;

        var merged = new List<(PromptRole role, string content)>();

        for (int i = 0; i < messages.Count; i++)
        {
            var (role, content) = messages[i];
            bool forceBreak = (mergeBoundary >= 0 && i == mergeBoundary && merged.Count > 0);

            if (!forceBreak && merged.Count > 0 && merged[^1].role == role)
            {
                var last = merged[^1];
                merged[^1] = (role, last.content + "\n\n" + content);
            }
            else
            {
                merged.Add((role, content));
            }
        }

        return merged;
    }
}
