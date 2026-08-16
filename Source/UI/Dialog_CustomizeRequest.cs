using RimTalk.Client;
using RimTalk.Util;
using UnityEngine;
using Verse;

namespace RimTalk.UI;

public class Dialog_CustomizeRequest : Window
{
    private readonly ApiConfig _config;
    private string _jsonText;
    private Vector2 _scrollPos = Vector2.zero;
    private GUIStyle _monoStyle;

    public Dialog_CustomizeRequest(ApiConfig config)
    {
        _config = config;
        _jsonText = string.IsNullOrWhiteSpace(config.CustomRequestJson)
            ? config.GetDefaultRequestJson()
            : config.CustomRequestJson;

        doCloseX = true;
        draggable = true;
        closeOnAccept = false;
        closeOnCancel = true;
        absorbInputAroundWindow = false;
        preventCameraMotion = false;
    }

    public override Vector2 InitialSize => new(660f, 540f);

    public override void DoWindowContents(Rect inRect)
    {
        InitStyles();

        // 1. Title
        Text.Font = GameFont.Medium;
        string modelName = _config.GetEffectiveModelName();
        string title = "RimTalk.Settings.CustomizeRequestTitle".Translate(_config.Provider.GetLabel(), modelName);
        Rect titleRect = new Rect(0f, 0f, inRect.width, 32f);
        Widgets.Label(titleRect, title);

        // 2. Description
        Text.Font = GameFont.Tiny;
        GUI.color = new Color(0.8f, 0.8f, 0.8f);
        Rect descRect = new Rect(0f, 34f, inRect.width, 36f);
        Widgets.Label(descRect, "RimTalk.Settings.CustomizeRequestDesc".Translate());
        GUI.color = Color.white;
        Text.Font = GameFont.Small;

        // 3. Text Editor Box
        float buttonBarHeight = 35f;
        float statusHeight = 24f;
        float editorY = 74f;
        float editorHeight = inRect.height - editorY - buttonBarHeight - statusHeight - 15f;

        Rect editorBoxRect = new Rect(0f, editorY, inRect.width, editorHeight);
        Widgets.DrawBoxSolid(editorBoxRect, new Color(0.12f, 0.12f, 0.12f, 0.8f));
        Widgets.DrawBox(editorBoxRect);

        float innerWidth = editorBoxRect.width - 16f;
        float textCalcHeight = _monoStyle.CalcHeight(new GUIContent(string.IsNullOrEmpty(_jsonText) ? " " : _jsonText), innerWidth);
        float contentHeight = Mathf.Max(editorBoxRect.height, textCalcHeight + 20f);

        Rect viewRect = new Rect(0f, 0f, innerWidth, contentHeight);
        Widgets.BeginScrollView(editorBoxRect, ref _scrollPos, viewRect);
        
        Rect textRect = new Rect(4f, 4f, innerWidth - 8f, contentHeight - 8f);
        _jsonText = GUI.TextArea(textRect, _jsonText, _monoStyle);

        Widgets.EndScrollView();

        // 4. Status Indicator (below editor)
        Rect statusRect = new Rect(0f, editorBoxRect.yMax + 4f, inRect.width, statusHeight);
        DrawStatus(statusRect);

        // 5. Button Bar (bottom)
        Rect buttonBarRect = new Rect(0f, inRect.height - buttonBarHeight, inRect.width, buttonBarHeight);
        DrawButtonBar(buttonBarRect);
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

    private void DrawButtonBar(Rect rect)
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
        x += resetWidth + 5f;

        // Format JSON
        const float formatWidth = 90f;
        if (Widgets.ButtonText(new Rect(x, y, formatWidth, h), "RimTalk.Settings.FormatJson".Translate()))
        {
            _jsonText = JsonUtil.FormatJson(_jsonText);
        }
        x += formatWidth + 5f;

        // Clear Override
        const float clearWidth = 55f;
        if (Widgets.ButtonText(new Rect(x, y, clearWidth, h), "RimTalk.Settings.ClearOverride".Translate()))
        {
            _jsonText = "";
        }
        x += clearWidth + 5f;

        // Help & Samples Button
        const float helpWidth = 115f;
        if (Widgets.ButtonText(new Rect(x, y, helpWidth, h), "RimTalk.Settings.CustomizeHelpButton".Translate()))
        {
            Find.WindowStack.Add(new Dialog_CustomizeRequestHelp((sampleJson, merge) =>
            {
                if (merge)
                {
                    _jsonText = JsonUtil.MergeJson(_jsonText, sampleJson);
                    _jsonText = JsonUtil.FormatJson(_jsonText);
                }
                else
                {
                    _jsonText = sampleJson;
                }
            }));
        }

        // Cancel and Save buttons (Right-aligned)
        const float saveWidth = 80f;
        const float cancelWidth = 75f;

        float cancelX = rect.xMax - cancelWidth;
        float saveX = cancelX - saveWidth - 5f;

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
}
