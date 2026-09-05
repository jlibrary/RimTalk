using System.Collections.Generic;
using RimTalk.Client;
using RimTalk.Util;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimTalk.UI;

public class Dialog_CustomizeRequest : Window
{
    private readonly ApiConfig _config;
    private string _jsonText;
    private Vector2 _scrollPos = Vector2.zero;
    private Vector2 _previewScrollPos = Vector2.zero;
    private GUIStyle _monoStyle;
    private readonly List<Dialog_CustomizeRequestHelp.SampleEntry> _samples;
    private int _selectedSampleIndex;

    public Dialog_CustomizeRequest(ApiConfig config)
    {
        _config = config;
        _jsonText = string.IsNullOrWhiteSpace(config.CustomRequestJson)
            ? config.GetDefaultRequestJson()
            : config.CustomRequestJson;

        _samples = Dialog_CustomizeRequestHelp.GetSamples();
        _selectedSampleIndex = 0;

        doCloseX = true;
        draggable = true;
        closeOnAccept = false;
        closeOnCancel = true;
        absorbInputAroundWindow = false;
        preventCameraMotion = false;
    }

    public override Vector2 InitialSize => new(940f, 600f);

    public override void DoWindowContents(Rect inRect)
    {
        InitStyles();

        float bottomBarHeight = 32f;
        float bottomBarY = inRect.height - bottomBarHeight;
        float colGap = 16f;
        float leftWidth = 500f;
        float rightX = leftWidth + colGap;
        float rightWidth = inRect.width - rightX;
        // Left editor box bottom aligns with: bottomBarY - statusHeight(22f) - 4f(margin) - 4f(margin)
        // statusY = bottomBarY - 26f, editor ends at statusY - 4f = bottomBarY - 30f
        float editorBottomY = bottomBarY - 30f;

        // 1. Left Column: JSON Editor
        DrawLeftColumn(0f, 0f, leftWidth, bottomBarY);

        // 2. Right Column: Templates Master-Detail
        DrawRightColumn(rightX, 0f, rightWidth, editorBottomY);

        // 3. Bottom Bar: Actions
        DrawBottomBar(new Rect(0f, bottomBarY, inRect.width, bottomBarHeight));
    }

    private void DrawLeftColumn(float x, float y, float width, float bottomLimitY)
    {
        // Title
        Text.Font = GameFont.Medium;
        string modelName = _config.GetEffectiveModelName();
        string title = "RimTalk.Settings.CustomizeRequestTitle".Translate(_config.Provider.GetLabel(), modelName);
        Rect titleRect = new Rect(x, y + 2f, width, 26f);
        Widgets.Label(titleRect, title);

        // Description
        Text.Font = GameFont.Tiny;
        GUI.color = new Color(0.72f, 0.72f, 0.72f);
        string desc = "RimTalk.Settings.CustomizeRequestDesc".Translate();
        float descH = Text.CalcHeight(desc, width);
        Rect descRect = new Rect(x, y + 30f, width, descH);
        Widgets.Label(descRect, desc);
        GUI.color = Color.white;
        Text.Font = GameFont.Small;

        // Status Indicator (above bottom limit)
        float statusHeight = 22f;
        float statusY = bottomLimitY - statusHeight - 4f;
        Rect statusRect = new Rect(x, statusY, width, statusHeight);
        DrawStatus(statusRect);

        // Text Editor Box
        float editorY = y + 30f + descH + 8f;
        float editorHeight = statusY - editorY - 4f;

        Rect editorBoxRect = new Rect(x, editorY, width, editorHeight);
        Widgets.DrawBoxSolid(editorBoxRect, new Color(0.08f, 0.08f, 0.08f, 0.85f));
        Color prevBoxCol = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, 0.12f);
        Widgets.DrawBox(editorBoxRect);
        GUI.color = prevBoxCol;

        float innerWidth = editorBoxRect.width - 16f;
        float textCalcHeight = _monoStyle.CalcHeight(new GUIContent(string.IsNullOrEmpty(_jsonText) ? " " : _jsonText), innerWidth);
        float contentHeight = Mathf.Max(editorBoxRect.height, textCalcHeight + 20f);

        Rect viewRect = new Rect(0f, 0f, innerWidth, contentHeight);
        Widgets.BeginScrollView(editorBoxRect, ref _scrollPos, viewRect);

        Rect textRect = new Rect(4f, 4f, innerWidth - 8f, contentHeight - 8f);
        _jsonText = GUI.TextArea(textRect, _jsonText, _monoStyle);

        Widgets.EndScrollView();
    }

    private void DrawRightColumn(float x, float y, float width, float bottomLimitY)
    {
        // Header
        Text.Font = GameFont.Small;
        Widgets.Label(new Rect(x, y + 2f, width, 22f), "RimTalk.Settings.CustomizeTemplatesTitle".Translate());
        Text.Font = GameFont.Tiny;
        GUI.color = new Color(0.72f, 0.72f, 0.72f);
        string sub = "RimTalk.Settings.CustomizeTemplatesSubtitle".Translate();
        float subH = Text.CalcHeight(sub, width);
        Widgets.Label(new Rect(x, y + 26f, width, subH), sub);
        GUI.color = Color.white;

        // Template List (Master)
        float listY = y + 26f + subH + 6f;
        float rowH = 26f;
        for (int i = 0; i < _samples.Count; i++)
        {
            Rect rowRect = new Rect(x, listY + i * (rowH + 3f), width, rowH);
            bool isSelected = (i == _selectedSampleIndex);
            if (isSelected)
            {
                Widgets.DrawBoxSolid(rowRect, new Color(0.2f, 0.28f, 0.38f, 0.7f));
                Widgets.DrawHighlightSelected(rowRect);
            }
            else
            {
                Widgets.DrawHighlightIfMouseover(rowRect);
                Widgets.DrawBoxSolid(rowRect, new Color(0.14f, 0.14f, 0.14f, 0.45f));
            }
            Color prevRowCol = GUI.color;
            GUI.color = isSelected ? new Color(0.45f, 0.7f, 1f, 0.45f) : new Color(1f, 1f, 1f, 0.08f);
            Widgets.DrawBox(rowRect);
            GUI.color = prevRowCol;

            Text.Font = GameFont.Tiny;
            GUI.color = isSelected ? Color.white : new Color(0.85f, 0.85f, 0.85f);
            Rect labelRect = new Rect(rowRect.x + 8f, rowRect.y + 4f, rowRect.width - 16f, rowRect.height - 8f);
            Widgets.Label(labelRect, _samples[i].Title);
            GUI.color = Color.white;

            if (Widgets.ButtonInvisible(rowRect))
            {
                _selectedSampleIndex = i;
                _previewScrollPos = Vector2.zero;
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
        }

        // Divider
        float dividerY = listY + _samples.Count * (rowH + 3f) + 6f;
        Color prevDividerCol = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, 0.1f);
        Widgets.DrawLineHorizontal(x, dividerY, width);
        GUI.color = prevDividerCol;

        // Detail Section (Detail)
        if (_selectedSampleIndex >= 0 && _selectedSampleIndex < _samples.Count)
        {
            var sample = _samples[_selectedSampleIndex];
            float detailY = dividerY + 8f;

            // Template Title
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(x, detailY, width, 22f), sample.Title);
            detailY += 24f;

            // Description
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.85f, 0.85f, 0.85f);
            float descH = Text.CalcHeight(sample.Description, width);
            Widgets.Label(new Rect(x, detailY, width, descH), sample.Description);
            detailY += descH + 3f;

            // Note (if any)
            if (!string.IsNullOrEmpty(sample.Note))
            {
                GUI.color = new Color(0.95f, 0.75f, 0.4f);
                float noteH = Text.CalcHeight(sample.Note, width);
                Widgets.Label(new Rect(x, detailY, width, noteH), sample.Note);
                detailY += noteH + 4f;
            }
            GUI.color = Color.white;

            // Action Buttons: Aligned so the bottom of these buttons matches the bottom of the left text editor box
            // bottomLimitY is passed as editorBottomY (the bottom of the left editor box)
            float templateBtnH = 28f;
            float templateBtnY = bottomLimitY - templateBtnH;

            float btnGap = 8f;
            float btnW = (width - btnGap) / 2f;

            if (Widgets.ButtonText(new Rect(x, templateBtnY, btnW, templateBtnH), "RimTalk.Settings.SampleUseTemplate".Translate()))
            {
                _jsonText = sample.Json;
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            if (Widgets.ButtonText(new Rect(x + btnW + btnGap, templateBtnY, btnW, templateBtnH), "RimTalk.Settings.SampleMergeTemplate".Translate()))
            {
                _jsonText = JsonUtil.MergeJson(_jsonText, sample.Json);
                _jsonText = JsonUtil.FormatJson(_jsonText);
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            // Monospace Code Preview Box (Selectable & Copyable)
            float codeBoxY = detailY;
            float codeBoxH = templateBtnY - 6f - codeBoxY;
            if (codeBoxH > 30f)
            {
                Rect codeBoxRect = new Rect(x, codeBoxY, width, codeBoxH);
                Widgets.DrawBoxSolid(codeBoxRect, new Color(0.08f, 0.08f, 0.08f, 0.85f));

                Color prevBoxBorder = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, 0.12f);
                Widgets.DrawBox(codeBoxRect);
                GUI.color = prevBoxBorder;

                float innerPreviewW = codeBoxRect.width - 16f;
                float jsonH = _monoStyle.CalcHeight(new GUIContent(sample.Json), innerPreviewW);
                Rect previewViewRect = new Rect(0f, 0f, innerPreviewW, Mathf.Max(codeBoxH, jsonH + 12f));

                Widgets.BeginScrollView(codeBoxRect, ref _previewScrollPos, previewViewRect);
                // GUI.TextArea allows text selection and Ctrl+C / Cmd+C copying
                GUI.TextArea(new Rect(4f, 4f, innerPreviewW - 8f, Mathf.Max(jsonH, codeBoxH - 8f)), sample.Json, _monoStyle);
                Widgets.EndScrollView();
            }
        }
    }

    private void DrawBottomBar(Rect rect)
    {
        float x = rect.x;
        float y = rect.y;
        float h = rect.height;

        // Reset to Default
        const float resetWidth = 115f;
        if (Widgets.ButtonText(new Rect(x, y, resetWidth, h), "RimTalk.Settings.ResetToDefault".Translate()))
        {
            _jsonText = _config.GetDefaultRequestJson();
        }
        x += resetWidth + 6f;

        // Format JSON
        const float formatWidth = 90f;
        if (Widgets.ButtonText(new Rect(x, y, formatWidth, h), "RimTalk.Settings.FormatJson".Translate()))
        {
            _jsonText = JsonUtil.FormatJson(_jsonText);
        }

        // Cancel and Save buttons (Right-aligned)
        const float saveWidth = 85f;
        const float cancelWidth = 75f;

        float cancelX = rect.xMax - cancelWidth;
        float saveX = cancelX - saveWidth - 6f;

        // Save Button
        bool isValid = JsonUtil.IsValidJson(_jsonText, out _);
        if (Widgets.ButtonText(new Rect(saveX, y, saveWidth, h), "RimTalk.Settings.CustomJsonSave".Translate(), active: isValid))
        {
            _config.CustomRequestJson = string.IsNullOrWhiteSpace(_jsonText) ? "" : _jsonText.Trim();
            AIClientFactory.Clear();
            Close();
        }

        // Cancel Button
        if (Widgets.ButtonText(new Rect(cancelX, y, cancelWidth, h), "RimTalk.Settings.CustomJsonCancel".Translate()))
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
                fontSize = 12,
                wordWrap = true,
                alignment = TextAnchor.UpperLeft,
                normal = { textColor = new Color(0.92f, 0.92f, 0.92f) }
            };
        }
    }

    private void DrawStatus(Rect rect)
    {
        Text.Font = GameFont.Tiny;
        Color prevColor = GUI.color;

        if (string.IsNullOrWhiteSpace(_jsonText))
        {
            GUI.color = Color.gray;
            Widgets.Label(rect, "RimTalk.Settings.CustomJsonEmpty".Translate());
        }
        else if (JsonUtil.IsValidJson(_jsonText, out var err))
        {
            GUI.color = new Color(0.4f, 0.9f, 0.4f);
            Widgets.Label(rect, "RimTalk.Settings.CustomJsonValid".Translate());
        }
        else
        {
            GUI.color = new Color(1f, 0.5f, 0.4f);
            Widgets.Label(rect, "RimTalk.Settings.CustomJsonInvalid".Translate(err));
        }

        GUI.color = prevColor;
        Text.Font = GameFont.Small;
    }
}
