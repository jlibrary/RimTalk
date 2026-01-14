using System;
using RimTalk.Prompt;
using Verse;

namespace RimTalk;

public static class PresetPreviewGenerator
{
    public static string GeneratePreview(string template)
    {
        if (string.IsNullOrWhiteSpace(template)) return "";
        
        if (Current.ProgramState != ProgramState.Playing)
        {
            return "RimTalk.Settings.PromptPreset.PreviewNotAvailable".Translate();
        }

        // 1. Leverage the last properly generated PromptContext
        PromptContext ctx = PromptManager.LastContext;

        // 2. If no request has been sent yet, inform the user
        if (ctx == null)
        {
            return "RimTalk.Settings.PromptPreset.NoRecentInteraction".Translate();
        }

        try
        {
            // Mark as preview to handle UI-specific logic (like chat history placeholders)
            ctx.IsPreview = true;
            return ScribanParser.Render(template, ctx, logErrors: false);
        }
        catch (Exception ex)
        {
            return "RimTalk.Settings.PromptPreset.PreviewError".Translate(ex.Message);
        }
    }
}
