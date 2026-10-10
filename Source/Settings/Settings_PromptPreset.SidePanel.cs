using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using RimTalk.Prompt;
using RimTalk.UI;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimTalk;

public partial class Settings
{
    private void DrawSidePanel(Rect rect, PromptManager manager, PromptEntry entry)
    {
        Widgets.DrawBoxSolid(rect, new Color(0.1f, 0.1f, 0.1f, 0.5f));
        Rect contentRect = rect.ContractedBy(5f);

        if (_sidePanelMode == 0)
        {
            // Variables
            DrawVariablePreviewContent(contentRect, manager, entry);
        }
        else
        {
            // Help
            string text = "RimTalk.Settings.AdvancedHelpContent".Translate();
            Text.Font = GameFont.Small;
            float viewWidth = contentRect.width - 16f;
            float height = Text.CalcHeight(text, viewWidth);

            Rect viewRect = new Rect(0f, 0f, viewWidth, height);

            Widgets.BeginScrollView(contentRect, ref _auxScrollPos, viewRect);
            Widgets.TextArea(viewRect, text, readOnly: true);
            Widgets.EndScrollView();
        }
    }

    private void DrawVariablePreviewContent(Rect rect, PromptManager manager, PromptEntry entry)
    {
        // 1. Search Bar
        Rect searchRect = new Rect(rect.x, rect.y, rect.width, 24f);
        _variableSearchQuery = Widgets.TextField(searchRect, _variableSearchQuery);
        if (string.IsNullOrEmpty(_variableSearchQuery))
        {
            GUI.color = new Color(1, 1, 1, 0.3f);
            Widgets.Label(searchRect.ContractedBy(2f, 0f), "RimTalk.Settings.PromptPreset.SearchPlaceholder".Translate());
            GUI.color = Color.white;
        }

        Text.Font = GameFont.Tiny;
        GUI.color = Color.gray;
        Widgets.Label(new Rect(rect.x, rect.y + 26f, rect.width, 20f),
            "RimTalk.Settings.PromptPreset.VariablePreviewHint".Translate());
        GUI.color = Color.white;
        Text.Font = GameFont.Small;

        Rect listRect = new Rect(rect.x, rect.y + 45f, rect.width, rect.height - 45f);
        var builtin = VariableDefinitions.GetScribanVariables();

        // 2. Filter Logic
        string query = _variableSearchQuery.Trim().ToLowerInvariant();
        var filteredBuiltin = new Dictionary<string, List<(string, string)>>();

        foreach (var cat in builtin)
        {
            var matches = cat.Value.Where(v =>
                v.Item1.ToLowerInvariant().Contains(query) ||
                v.Item2.ToLowerInvariant().Contains(query) ||
                cat.Key.ToLowerInvariant().Contains(query)
            ).ToList();

            if (matches.Any()) filteredBuiltin[cat.Key] = matches;
        }

        // --- Dynamic Variable Discovery ---
        if (_variableSearchQuery.Contains("."))
        {
            var dynamicVars = VariableDefinitions.GetDynamicVariables(_variableSearchQuery, entry.Content);
            foreach (var kvp in dynamicVars)
            {
                filteredBuiltin[kvp.Key] = kvp.Value;
            }
        }

        var runtimeVars = manager.VariableStore.GetAllVariables()
            .Where(kvp => kvp.Key.ToLowerInvariant().Contains(query) || kvp.Value.ToLowerInvariant().Contains(query))
            .ToList();

        // 3. Dynamic height calculation
        float totalRows = filteredBuiltin.Sum(c => c.Value.Count + 1);
        if (runtimeVars.Any()) totalRows += runtimeVars.Count + 1;

        Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, totalRows * 22f);

        Widgets.BeginScrollView(listRect, ref _auxScrollPos, viewRect);
        float vy = 0f;

        // 4. Render Filtered Builtin
        string prefixToStrip = "";
        int lastDotIndex = _variableSearchQuery.LastIndexOf('.');
        if (lastDotIndex >= 0)
        {
            prefixToStrip = _variableSearchQuery.Substring(0, lastDotIndex + 1);
        }

        foreach (var cat in filteredBuiltin)
        {
            GUI.color = Color.cyan;
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(0f, vy, viewRect.width, 20f), $"▼ {cat.Key}");
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            vy += 22f;
            foreach (var v in cat.Value)
            {
                string displayLabel = null;
                if (!string.IsNullOrEmpty(prefixToStrip) && v.Item1.StartsWith(prefixToStrip, StringComparison.OrdinalIgnoreCase))
                {
                    displayLabel = v.Item1.Substring(prefixToStrip.Length);
                }
                DrawVariableRow(ref vy, viewRect.width, v.Item1, v.Item2, null, entry, displayLabel);
            }
        }

        // 5. Render Filtered Runtime
        if (runtimeVars.Any())
        {
            GUI.color = Color.green;
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(0f, vy, viewRect.width, 20f), "▼ Runtime Variables");
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            vy += 22f;
            foreach (var kvp in runtimeVars) DrawVariableRow(ref vy, viewRect.width, kvp.Key, "", kvp.Value, entry);
        }

        Widgets.EndScrollView();
    }

    private void DrawVariableRow(ref float y, float w, string n, string d, string v, PromptEntry entry, string displayLabel = null)
    {
        Rect rowRect = new Rect(0f, y, w, 20f);
        if (Mouse.IsOver(rowRect)) Widgets.DrawHighlight(rowRect);

        string tooltipText;
        if (!string.IsNullOrEmpty(d) && !string.IsNullOrEmpty(v))
            tooltipText = $"{{{{ {n} }}}}\n{d}\n{v}";
        else if (!string.IsNullOrEmpty(d))
            tooltipText = $"{{{{ {n} }}}}\n{d}";
        else if (!string.IsNullOrEmpty(v))
            tooltipText = $"{{{{ {n} }}}}\n{v}";
        else
            tooltipText = $"{{{{ {n} }}}}";

        TooltipHandler.TipRegion(rowRect, tooltipText);

        if (Widgets.ButtonInvisible(rowRect))
        {
            SoundDefOf.Click.PlayOneShotOnCamera(null);
            InsertVariable(n, entry);
        }

        Text.Font = GameFont.Tiny;
        
        string label = displayLabel ?? n;
        string fullVar = $"{{{{ {label} }}}}";
        float labelWidth = Text.CalcSize(fullVar).x;

        // 1. Draw Variable Name
        GUI.color = new Color(0.8f, 1f, 0.8f);
        Widgets.Label(new Rect(2f, y, labelWidth + 5f, 20f), fullVar);

        // 2. Draw Type Info/Params right next to it
        string typeInfo = v ?? d;
        if (!string.IsNullOrEmpty(typeInfo))
        {
            GUI.color = new Color(0.5f, 0.5f, 0.5f);
            float typeX = labelWidth + 10f;
            float typeW = w - typeX - 5f;
            if (typeW > 10f)
            {
                Widgets.Label(new Rect(typeX, y, typeW, 20f), typeInfo);
            }
        }

        GUI.color = Color.white;
        Text.Font = GameFont.Small;
        y += 20f;
    }

    private void InsertVariable(string variableName, PromptEntry entry)
    {
        // 1. Play Sound
        SoundDefOf.Click.PlayOneShotOnCamera(null);

        // 2. Get Editor State
        TextEditor te = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);

        if (te != null && te.cursorIndex >= 0 && te.cursorIndex <= entry.Content.Length)
        {
            string text = entry.Content;
            int cursor = te.cursorIndex;

            // --- Step A: Identify and Remove Prefix (Autocomplete) ---
            // Find the partial word to the left (e.g., "pawn.Is")
            int start = cursor - 1;
            while (start >= 0)
            {
                char c = text[start];
                if (!char.IsLetterOrDigit(c) && c != '.' && c != '_') break;
                start--;
            }

            start++;

            string prefix = "";
            if (start < cursor) prefix = text.Substring(start, cursor - start);

            // Check if the variable starts with what we typed
            bool isMatch = !string.IsNullOrEmpty(prefix) &&
                           variableName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

            // If it's a match, delete what we typed so we can replace it cleanly
            if (isMatch)
            {
                text = text.Remove(start, cursor - start);
                cursor = start;
            }

            // --- Step B: Check Surroundings (Context) ---

            // Scan backwards ignoring whitespace to see if we are inside {{
            int ptr = cursor - 1;
            while (ptr >= 0 && char.IsWhiteSpace(text[ptr])) ptr--;

            bool hasOpenBrackets = (ptr >= 1 && text[ptr] == '{' && text[ptr - 1] == '{');

            // --- Step C: Build the String to Insert ---
            string finalInsert;

            if (hasOpenBrackets)
            {
                // We are already inside brackets (e.g., "{{pawn.Is|")

                // 1. Ensure space after {{
                // If the character immediately to the left is '{', add a space
                bool needsLeftSpace = (cursor > 0 && text[cursor - 1] == '{');
                finalInsert = (needsLeftSpace ? " " : "") + variableName;

                // 2. Ensure closing }}
                // Scan forward to see if }} exists reasonably close
                int endPtr = cursor;
                while (endPtr < text.Length && char.IsWhiteSpace(text[endPtr])) endPtr++;

                bool hasClosingBrackets = (endPtr < text.Length - 1 && text[endPtr] == '}' && text[endPtr + 1] == '}');

                if (!hasClosingBrackets)
                {
                    finalInsert += " }}";
                }
            }
            else
            {
                // Not inside brackets. Insert the full valid block.
                finalInsert = $"{{{{ {variableName} }}}}";
            }

            // --- Step D: Apply and Fix Cursor ---
            text = text.Insert(cursor, finalInsert);
            entry.Content = text;

            // Determine final cursor position: Always after the "}}"
            // If we added "}}", it's at the end of inserted text.
            // If "}}" already existed, we need to find them and jump past them.
            int newCursorPos;

            if (hasOpenBrackets)
            {
                // Find the first }} after our insertion point
                int closeIndex = text.IndexOf("}}", cursor);
                if (closeIndex != -1)
                    newCursorPos = closeIndex + 2; // Jump past }}
                else
                    newCursorPos = cursor + finalInsert.Length;
            }
            else
            {
                newCursorPos = cursor + finalInsert.Length;
            }

            // Apply cursor change
            te.text = text; // Update internal TE text immediately
            te.cursorIndex = newCursorPos;
            te.selectIndex = newCursorPos;
        }
        else
        {
            // Fallback if no focus
            entry.Content += $"{{{{ {variableName} }}}}";
        }
    }
}
