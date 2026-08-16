using System;
using System.Collections.Generic;
using RimTalk.Util;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimTalk.UI;

public class Dialog_CustomizeRequestHelp : Window
{
    private readonly Action<string, bool> _onApplySample;
    private Vector2 _scrollPos = Vector2.zero;
    private GUIStyle _monoStyle;

    private struct SampleEntry
    {
        public string Title;
        public string Description;
        public string Note;
        public string Json;
    }

    private static readonly List<SampleEntry> Samples =
    [
        new SampleEntry
        {
            Title = "RimTalk.Settings.SampleBasicTitle".Translate(),
            Description = "RimTalk.Settings.SampleBasicDesc".Translate(),
            Note = "RimTalk.Settings.SampleBasicNote".Translate(),
            Json = "{\n  \"temperature\": 0.7,\n  \"top_p\": 0.9,\n  \"max_tokens\": 2048,\n  \"presence_penalty\": 0.2\n}"
        },
        new SampleEntry
        {
            Title = "RimTalk.Settings.SampleReasoningEffortTitle".Translate(),
            Description = "RimTalk.Settings.SampleReasoningEffortDesc".Translate(),
            Note = "RimTalk.Settings.SampleReasoningEffortNote".Translate(),
            Json = "{\n  \"temperature\": 1.0,\n  \"reasoning_effort\": \"minimal\"\n}"
        },
        new SampleEntry
        {
            Title = "RimTalk.Settings.SampleDisableThinkingTitle".Translate(),
            Description = "RimTalk.Settings.SampleDisableThinkingDesc".Translate(),
            Note = "RimTalk.Settings.SampleDisableThinkingNote".Translate(),
            Json = "{\n  \"thinking\": {\n    \"type\": \"disabled\"\n  }\n}"
        },
        new SampleEntry
        {
            Title = "RimTalk.Settings.SampleEnableThinkingTitle".Translate(),
            Description = "RimTalk.Settings.SampleEnableThinkingDesc".Translate(),
            Note = "RimTalk.Settings.SampleEnableThinkingNote".Translate(),
            Json = "{\n  \"thinking\": {\n    \"type\": \"enabled\",\n    \"budget_tokens\": 2048\n  }\n}"
        },
        new SampleEntry
        {
            Title = "RimTalk.Settings.SampleStructuredOutputTitle".Translate(),
            Description = "RimTalk.Settings.SampleStructuredOutputDesc".Translate(),
            Note = "RimTalk.Settings.SampleStructuredOutputNote".Translate(),
            Json = "{\n  \"response_format\": {\n    \"type\": \"json_schema\",\n    \"json_schema\": {\n      \"name\": \"talk_response\",\n      \"strict\": true,\n      \"schema\": {\n        \"type\": \"array\",\n        \"items\": {\n          \"type\": \"object\",\n          \"required\": [\"name\", \"text\"],\n          \"properties\": {\n            \"name\": { \"type\": \"string\" },\n            \"text\": { \"type\": \"string\" },\n            \"act\": {\n              \"type\": \"string\",\n              \"enum\": [\"Insult\", \"Slight\", \"Chat\", \"Kind\"]\n            },\n            \"target\": { \"type\": \"string\" }\n          },\n          \"additionalProperties\": false\n        }\n      }\n    }\n  }\n}"
        },
        new SampleEntry
        {
            Title = "RimTalk.Settings.SampleLocalTitle".Translate(),
            Description = "RimTalk.Settings.SampleLocalDesc".Translate(),
            Note = "RimTalk.Settings.SampleLocalNote".Translate(),
            Json = "{\n  \"temperature\": 0.8,\n  \"top_k\": 40,\n  \"num_predict\": 1024\n}"
        }
    ];

    public Dialog_CustomizeRequestHelp(Action<string, bool> onApplySample = null)
    {
        _onApplySample = onApplySample;
        doCloseX = true;
        draggable = true;
        closeOnAccept = false;
        closeOnCancel = true;
        absorbInputAroundWindow = false;
        preventCameraMotion = false;
    }

    public override Vector2 InitialSize => new(700f, 620f);

    public override void DoWindowContents(Rect inRect)
    {
        InitStyles();

        // 1. Title
        Text.Font = GameFont.Medium;
        Rect titleRect = new Rect(0f, 0f, inRect.width, 32f);
        Widgets.Label(titleRect, "RimTalk.Settings.CustomizeHelpTitle".Translate());

        // 2. Scrollable Content Area
        float buttonBarHeight = 35f;
        Rect scrollOuterRect = new Rect(0f, 36f, inRect.width, inRect.height - 36f - buttonBarHeight - 5f);

        // Pre-calculate content height dynamically
        float innerWidth = scrollOuterRect.width - 16f;
        float totalHeight = CalculateContentHeight(innerWidth);

        Widgets.BeginScrollView(scrollOuterRect, ref _scrollPos, new Rect(0f, 0f, innerWidth, totalHeight));

        float curY = 0f;

        // Warning / Disclaimer Box
        curY = DrawDisclaimerBox(0f, curY, innerWidth);
        curY += 10f;

        // Samples list
        for (int i = 0; i < Samples.Count; i++)
        {
            curY = DrawSampleCard(0f, curY, innerWidth, Samples[i]);
            curY += 12f;
        }

        Widgets.EndScrollView();

        // Close Button
        Rect closeBtnRect = new Rect(inRect.width - 100f, inRect.height - buttonBarHeight, 100f, buttonBarHeight);
        if (Widgets.ButtonText(closeBtnRect, "RimTalk.Settings.CustomJsonCancel".Translate()))
        {
            Close();
        }
    }

    private void InitStyles()
    {
        if (_monoStyle == null)
        {
            _monoStyle = new GUIStyle(Text.fontStyles[(int)GameFont.Tiny])
            {
                fontSize = 11,
                wordWrap = true,
                alignment = TextAnchor.UpperLeft,
                normal = { textColor = new Color(0.9f, 0.9f, 0.9f) }
            };
        }
    }

    private float CalculateContentHeight(float width)
    {
        float h = 0f;
        // Disclaimer box
        string disText = "RimTalk.Settings.CustomizeHelpDisclaimerText".Translate();
        h += 30f + Text.CalcHeight(disText, width - 20f) + 16f;
        h += 10f;

        // Samples
        foreach (var sample in Samples)
        {
            h += 24f; // title
            h += Text.CalcHeight(sample.Description, width - 16f) + 4f;
            if (!string.IsNullOrEmpty(sample.Note))
                h += Text.CalcHeight(sample.Note, width - 16f) + 4f;

            float jsonH = _monoStyle.CalcHeight(new GUIContent(sample.Json), width - 24f) + 16f;
            h += jsonH + 34f + 16f; // json box + buttons + margins
            h += 12f;
        }

        return h + 40f;
    }

    private float DrawDisclaimerBox(float x, float y, float width)
    {
        string header = "RimTalk.Settings.CustomizeHelpDisclaimerTitle".Translate();
        string text = "RimTalk.Settings.CustomizeHelpDisclaimerText".Translate();

        Text.Font = GameFont.Tiny;
        float textH = Text.CalcHeight(text, width - 20f);
        float boxH = 26f + textH + 12f;

        Rect boxRect = new Rect(x, y, width, boxH);
        Widgets.DrawBoxSolid(boxRect, new Color(0.35f, 0.22f, 0.05f, 0.5f));
        
        Color prevColor = GUI.color;
        GUI.color = new Color(1f, 0.75f, 0.2f);
        Widgets.DrawBox(boxRect);

        // Header
        Text.Font = GameFont.Small;
        Rect headerRect = new Rect(x + 10f, y + 6f, width - 20f, 22f);
        Widgets.Label(headerRect, header);

        // Text
        Text.Font = GameFont.Tiny;
        GUI.color = new Color(0.95f, 0.9f, 0.8f);
        Rect textRect = new Rect(x + 10f, y + 26f, width - 20f, textH);
        Widgets.Label(textRect, text);

        GUI.color = prevColor;
        Text.Font = GameFont.Small;
        return y + boxH;
    }

    private float DrawSampleCard(float x, float y, float width, SampleEntry sample)
    {
        float startY = y;

        // Title
        Text.Font = GameFont.Small;
        Rect titleRect = new Rect(x + 4f, y, width - 8f, 22f);
        Widgets.Label(titleRect, sample.Title);
        y += 24f;

        // Description
        Text.Font = GameFont.Tiny;
        GUI.color = new Color(0.85f, 0.85f, 0.85f);
        float descH = Text.CalcHeight(sample.Description, width - 16f);
        Rect descRect = new Rect(x + 6f, y, width - 16f, descH);
        Widgets.Label(descRect, sample.Description);
        y += descH + 3f;

        // Note (if any)
        if (!string.IsNullOrEmpty(sample.Note))
        {
            GUI.color = new Color(0.95f, 0.75f, 0.4f);
            float noteH = Text.CalcHeight(sample.Note, width - 16f);
            Rect noteRect = new Rect(x + 6f, y, width - 16f, noteH);
            Widgets.Label(noteRect, sample.Note);
            y += noteH + 3f;
        }

        GUI.color = Color.white;

        // Monospace Code Box
        float jsonH = _monoStyle.CalcHeight(new GUIContent(sample.Json), width - 24f) + 16f;
        Rect codeBoxRect = new Rect(x + 4f, y, width - 8f, jsonH);
        Widgets.DrawBoxSolid(codeBoxRect, new Color(0.08f, 0.08f, 0.08f, 0.85f));
        Widgets.DrawBox(codeBoxRect);

        Rect codeTextRect = new Rect(codeBoxRect.x + 6f, codeBoxRect.y + 6f, codeBoxRect.width - 12f, codeBoxRect.height - 12f);
        GUI.Label(codeTextRect, sample.Json, _monoStyle);
        y += jsonH + 6f;

        // Action Buttons
        float btnH = 26f;
        float curBtnX = x + 4f;

        if (_onApplySample != null)
        {
            // Replace Button
            const float replaceW = 120f;
            if (Widgets.ButtonText(new Rect(curBtnX, y, replaceW, btnH), "RimTalk.Settings.SampleUseTemplate".Translate()))
            {
                _onApplySample(sample.Json, false);
                Close();
            }
            curBtnX += replaceW + 6f;

            // Merge Button
            const float mergeW = 140f;
            if (Widgets.ButtonText(new Rect(curBtnX, y, mergeW, btnH), "RimTalk.Settings.SampleMergeTemplate".Translate()))
            {
                _onApplySample(sample.Json, true);
                Close();
            }
            curBtnX += mergeW + 6f;
        }

        // Copy Button
        const float copyW = 80f;
        if (Widgets.ButtonText(new Rect(curBtnX, y, copyW, btnH), "RimTalk.Settings.SampleCopy".Translate()))
        {
            GUIUtility.systemCopyBuffer = sample.Json;
            Messages.Message("RimTalk.Settings.SampleCopied".Translate(), MessageTypeDefOf.TaskCompletion, false);
        }

        y += btnH + 6f;

        // Card frame outline
        Rect cardRect = new Rect(x, startY - 4f, width, (y - startY) + 8f);
        Widgets.DrawBoxSolid(cardRect, new Color(0.18f, 0.18f, 0.18f, 0.25f));
        Widgets.DrawBox(cardRect);

        return y;
    }
}
