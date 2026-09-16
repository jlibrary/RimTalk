using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using RimTalk.Client;
using RimTalk.Data;
using RimTalk.Service;
using RimTalk.Source.Data;
using RimTalk.Util;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using Cache = RimTalk.Data.Cache;
using Logger = RimTalk.Util.Logger;

namespace RimTalk.UI;

public class Dialog_ApiLog : Window
{
    private readonly Action _onResend;
    private readonly Action<string> _onResendCustomRequest;
    private readonly Payload _payload;
    private ApiLog _apiLog;
    private TalkRequest _trackedResendRequest;
    private string _lastLoadedResponse;

    private Payload CurrentPayload => _apiLog?.Payload ?? _payload;

    private bool IsResponseGenerating
    {
        get
        {
            if (_trackedResendRequest != null) return true;

            Payload p = CurrentPayload;
            if (!string.IsNullOrEmpty(p?.ErrorMessage)) return false;
            if (!string.IsNullOrEmpty(p?.Response)) return false;

            if (_apiLog != null)
            {
                return string.IsNullOrEmpty(_apiLog.Response) || AIService.IsBusy();
            }

            return false;
        }
    }

    // Request section state
    private string _initialRequestRaw;
    private string _requestRaw;
    private string _requestUnescaped;
    private bool _requestEditMode;
    private bool _requestUnescapeNewlines;
    private Vector2 _requestScrollPos = Vector2.zero;
    private bool _isJsonValid = true;
    private string _jsonSyntaxError;

    // Response section state
    private string _responseRaw;
    private Vector2 _responseScrollPos = Vector2.zero;

    // Search state
    private string _searchText = string.Empty;
    private int _currentMatchIndex = -1;
    private readonly List<SearchMatch> _matches = new();
    private float _lastRequestWidth = 800f;
    private float _lastResponseWidth = 800f;

    private struct SearchMatch
    {
        public bool IsRequest;
        public int CharIndex;
    }

    // GUI Styles
    private GUIStyle _viewMonoStyle;
    private GUIStyle _editMonoStyle;

    public Dialog_ApiLog(Payload payload, Action onResend = null)
    {
        _payload = payload;
        _onResend = onResend;

        InitPayloadStrings();

        doCloseX = true;
        draggable = true;
        resizeable = true;
        closeOnAccept = false;
        absorbInputAroundWindow = false;
        preventCameraMotion = false;
    }

    public Dialog_ApiLog(Payload payload, ApiLog apiLog, Action onResend = null, Action<string> onResendCustomRequest = null)
        : this(payload, onResend)
    {
        _apiLog = apiLog;
        _onResendCustomRequest = onResendCustomRequest;
        InitPayloadStrings();
    }

    private void InitPayloadStrings()
    {
        Payload p = CurrentPayload;
        _requestRaw = p?.Request != null ? JsonUtil.PrettifyJson(p.Request) : string.Empty;
        _initialRequestRaw = _requestRaw;
        _requestUnescaped = JsonUtil.UnescapeNewlines(_requestRaw);
        ValidateJson(_requestRaw);

        UpdateResponseStrings(p);
    }

    private void ValidateJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            _isJsonValid = true;
            _jsonSyntaxError = null;
            return;
        }
        _isJsonValid = JsonUtil.IsValidJson(json, out _jsonSyntaxError);
    }

    private void UpdateResponseStrings(Payload p)
    {
        _lastLoadedResponse = p?.Response;
        string resp = p?.Response;
        if (!string.IsNullOrEmpty(resp))
        {
            resp = resp.Replace("```jsonl", "").Replace("```json", "").Replace("```", "").Trim();
        }
        _responseRaw = resp != null ? JsonUtil.PrettifyJson(resp) : string.Empty;
        UpdateSearchMatches(scrollToMatch: false);
    }

    private void CheckTrackedResponse()
    {
        if (_trackedResendRequest != null)
        {
            foreach (var log in ApiHistory.GetAll())
            {
                if (log.TalkRequest == _trackedResendRequest)
                {
                    _apiLog = log;
                    _trackedResendRequest = null;
                    break;
                }
            }
        }

        Payload p = CurrentPayload;
        if (!string.Equals(p?.Response, _lastLoadedResponse, StringComparison.Ordinal))
        {
            UpdateResponseStrings(p);
        }
    }

    public override Vector2 InitialSize => new(840f, 720f);

    public override void DoWindowContents(Rect inRect)
    {
        InitStyles();
        CheckTrackedResponse();

        float y = 0f;

        // --- Top Bar: Title & Action Buttons ---
        Text.Font = GameFont.Medium;
        Widgets.Label(new Rect(0f, y, 200f, 28f), "RimTalk.DebugWindow.ApiLog".Translate());
        Text.Font = GameFont.Tiny;

        float btnW = 88f;
        float btnH = 26f;
        float rightPadding = 24f; // Space between rightmost button and window border / Close X

        bool showResend = _onResend != null || _onResendCustomRequest != null || _apiLog?.TalkRequest != null;
        bool hasError = (_apiLog != null && _apiLog.IsError) ||
                        !string.IsNullOrEmpty(CurrentPayload?.ErrorMessage) ||
                        (CurrentPayload?.StatusCode != null && CurrentPayload.StatusCode.Value >= 400);

        float curBtnX = inRect.width - rightPadding;

        // Report Button (Red button for reporting issue - disabled when no error)
        curBtnX -= btnW;
        var prevReportCol = GUI.color;
        if (hasError)
        {
            GUI.color = new Color(1f, 0.4f, 0.4f);
        }
        GUI.enabled = hasError;
        Rect reportRect = new Rect(curBtnX, y, btnW, btnH);
        if (Widgets.ButtonText(reportRect, "RimTalk.DebugWindow.Report".Translate()))
        {
            SoundDefOf.Click.PlayOneShotOnCamera();
            ConfirmAndReportIssue();
        }
        TooltipHandler.TipRegion(reportRect, "RimTalk.DebugWindow.ReportTooltip".Translate());
        GUI.enabled = true;
        GUI.color = prevReportCol;
        curBtnX -= 8f;

        // Resend Button
        if (showResend)
        {
            curBtnX -= btnW;
            var prevCol = GUI.color;
            GUI.color = new Color(0.6f, 0.9f, 0.6f);
            Rect resendRect = new Rect(curBtnX, y, btnW, btnH);
            bool canResend = _apiLog == null ? (_onResend != null || _onResendCustomRequest != null) : (_apiLog.Channel != Channel.User);
            if (_requestEditMode && !_isJsonValid)
            {
                canResend = false;
            }
            GUI.enabled = canResend;
            if (Widgets.ButtonText(resendRect, "RimTalk.DebugWindow.Resend".Translate()))
            {
                SoundDefOf.Click.PlayOneShotOnCamera();
                ExecuteResend();
            }
            TooltipHandler.TipRegion(resendRect, "RimTalk.DebugWindow.ResendTooltip".Translate());
            GUI.enabled = true;
            GUI.color = prevCol;
            curBtnX -= 8f;
        }

        // Copy All Button
        curBtnX -= btnW;
        Rect copyAllRect = new Rect(curBtnX, y, btnW, btnH);
        if (Widgets.ButtonText(copyAllRect, "RimTalk.DebugWindow.CopyAll".Translate()))
        {
            GUIUtility.systemCopyBuffer = BuildCompleteReport();
            Messages.Message("RimTalk.DebugWindow.Copied".Translate(), MessageTypeDefOf.TaskCompletion, false);
        }
        TooltipHandler.TipRegion(copyAllRect, "RimTalk.DebugWindow.CopyClipboardTooltip".Translate());

        y += 34f;

        // --- Section 1: API Info Header ---
        Payload p = CurrentPayload;
        float infoHeight = string.IsNullOrEmpty(p?.ErrorMessage) ? 58f : 78f;
        Rect infoRect = new Rect(0f, y, inRect.width, infoHeight);
        DrawApiInfoSection(infoRect);
        y += infoHeight + 8f;

        // --- Available Height for Request & Response (70% Request, 30% Response) ---
        float remainingHeight = inRect.height - y;
        float sectionSpacing = 10f;
        float usableHeight = Mathf.Max(200f, remainingHeight - sectionSpacing);
        float requestHeight = Mathf.Round(usableHeight * 0.70f);
        float responseHeight = usableHeight - requestHeight;

        // --- Section 2: Request Payload ---
        Rect requestRect = new Rect(0f, y, inRect.width, requestHeight);
        DrawRequestSection(requestRect);
        y += requestHeight + sectionSpacing;

        // --- Section 3: Response Payload ---
        Rect responseRect = new Rect(0f, y, inRect.width, responseHeight);
        DrawResponseSection(responseRect);
    }

    private void DrawApiInfoSection(Rect rect)
    {
        Payload p = CurrentPayload;
        Widgets.DrawBoxSolid(rect, new Color(0.09f, 0.09f, 0.11f, 0.85f));
        Color prevColor = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, 0.12f);
        Widgets.DrawBox(rect);
        GUI.color = prevColor;

        Rect inner = rect.ContractedBy(8f, 6f);
        Text.Font = GameFont.Tiny;

        float lineH = 18f;
        float curY = inner.y;

        // URL line
        Widgets.Label(new Rect(inner.x, curY, 50f, lineH), "URL:".Colorize(new Color(0.6f, 0.8f, 1f)));
        Widgets.Label(new Rect(inner.x + 52f, curY, inner.width - 52f, lineH), p?.URL ?? "-");
        curY += lineH;

        // Model & Tokens line
        float halfW = inner.width * 0.5f;
        Widgets.Label(new Rect(inner.x, curY, 50f, lineH), "Model:".Colorize(new Color(0.6f, 0.8f, 1f)));
        Widgets.Label(new Rect(inner.x + 52f, curY, halfW - 52f, lineH), p?.Model ?? "-");

        Widgets.Label(new Rect(inner.x + halfW, curY, 55f, lineH), "Tokens:".Colorize(new Color(0.6f, 0.8f, 1f)));
        string tokenText = (p?.TokenCount ?? 0) > 0 ? p.TokenCount.ToString() : (IsResponseGenerating ? "-" : "0");
        Widgets.Label(new Rect(inner.x + halfW + 57f, curY, halfW - 57f, lineH), tokenText);
        curY += lineH;

        // Error line (if any)
        if (!string.IsNullOrEmpty(p?.ErrorMessage))
        {
            Widgets.Label(new Rect(inner.x, curY, 50f, lineH), "Error:".Colorize(new Color(1f, 0.4f, 0.4f)));
            Widgets.Label(new Rect(inner.x + 52f, curY, inner.width - 52f, lineH), p.ErrorMessage.Colorize(new Color(1f, 0.45f, 0.45f)));
        }

        Text.Font = GameFont.Small;
    }

    private void DrawRequestSection(Rect rect)
    {
        float headerH = 24f;
        float headerY = rect.y;

        // Header Title
        Text.Font = GameFont.Tiny;
        GUI.color = new Color(0.5f, 0.85f, 1f);
        Widgets.Label(new Rect(rect.x, headerY + 2f, 175f, headerH), "--- REQUEST PAYLOAD (JSON) ---");
        GUI.color = Color.white;

        // Header Controls (Right-aligned)
        float curX = rect.xMax;

        // 1. Readable newlines toggle
        string unescapeLabel = "RimTalk.DebugWindow.UnescapeNewlines".Translate();
        float unescapeW = Text.CalcSize(unescapeLabel).x + 28f;
        curX -= unescapeW;
        if (DrawToggle(new Rect(curX, headerY, unescapeW, headerH), unescapeLabel, ref _requestUnescapeNewlines, "RimTalk.DebugWindow.UnescapeNewlinesTooltip".Translate()))
        {
            if (_requestUnescapeNewlines)
                _requestUnescaped = JsonUtil.UnescapeNewlines(_requestRaw);
            else
                _requestRaw = GetEffectiveRequestJson();

            ValidateJson(_requestUnescapeNewlines ? _requestUnescaped : _requestRaw);
            UpdateSearchMatches(scrollToMatch: false);
        }

        // 2. Edit mode toggle
        curX -= 10f;
        string editLabel = "RimTalk.DebugWindow.EditMode".Translate();
        float editW = Text.CalcSize(editLabel).x + 28f;
        curX -= editW;
        if (DrawToggle(new Rect(curX, headerY, editW, headerH), editLabel, ref _requestEditMode, "RimTalk.DebugWindow.EditModeTooltip".Translate()))
        {
            UpdateSearchMatches(scrollToMatch: false);
        }

        // 3. Undo button (icon)
        curX -= 10f;
        float iconSize = 20f;
        curX -= iconSize;
        Rect undoRect = new Rect(curX, headerY + 2f, iconSize, iconSize);
        bool canUndo = !string.Equals(_requestRaw, _initialRequestRaw, StringComparison.Ordinal) ||
                       !string.Equals(_requestUnescaped, JsonUtil.UnescapeNewlines(_initialRequestRaw), StringComparison.Ordinal);
        GUI.color = canUndo ? Color.white : new Color(1f, 1f, 1f, 0.35f);
        if (Widgets.ButtonImage(undoRect, TexButton.HotReloadDefs) && canUndo)
        {
            _requestRaw = _initialRequestRaw;
            _requestUnescaped = JsonUtil.UnescapeNewlines(_initialRequestRaw);
            ValidateJson(_requestUnescapeNewlines ? _requestUnescaped : _requestRaw);
            UpdateSearchMatches(scrollToMatch: false);
            SoundDefOf.Click.PlayOneShotOnCamera();
        }
        GUI.color = Color.white;
        TooltipHandler.TipRegion(undoRect, "RimTalk.DebugWindow.UndoTooltip".Translate());

        // 4. Copy button (icon)
        curX -= 6f;
        curX -= iconSize;
        Rect copyRect = new Rect(curX, headerY + 2f, iconSize, iconSize);
        if (Widgets.ButtonImage(copyRect, TexButton.Copy))
        {
            GUIUtility.systemCopyBuffer = GetEffectiveRequestJson();
            Messages.Message("RimTalk.DebugWindow.Copied".Translate(), MessageTypeDefOf.TaskCompletion, false);
        }
        TooltipHandler.TipRegion(copyRect, "RimTalk.DebugWindow.CopyClipboardTooltip".Translate());

        // 5. Middle area: Search bar (view mode) OR JSON validation status (edit mode)
        curX -= 12f;
        float middleX = rect.x + 200f;
        float middleW = curX - middleX;
        if (middleW > 60f)
        {
            if (_requestEditMode)
            {
                string statusLabel = _isJsonValid
                    ? "RimTalk.Settings.CustomJsonValid".Translate()
                    : "RimTalk.Settings.CustomJsonInvalid".Translate(_jsonSyntaxError ?? string.Empty);
                Text.Font = GameFont.Tiny;
                float statusTextW = Text.CalcSize(statusLabel).x + 12f;
                float statusW = Mathf.Clamp(statusTextW, 80f, middleW);
                float statusX = curX - statusW - 6f;

                Rect statusRect = new Rect(statusX, headerY + 2f, statusW, headerH);
                Color prevColor = GUI.color;
                GUI.color = _isJsonValid ? new Color(0.4f, 0.9f, 0.4f) : new Color(1f, 0.5f, 0.4f);
                Widgets.Label(statusRect, statusLabel);
                if (!_isJsonValid) TooltipHandler.TipRegion(statusRect, _jsonSyntaxError ?? string.Empty);
                GUI.color = prevColor;
            }
            else
            {
                bool hasSearch = !string.IsNullOrWhiteSpace(_searchText);
                float searchW = Mathf.Clamp(hasSearch ? 190f : 120f, 60f, middleW);
                float searchX = curX - searchW - 6f;
                DrawSearchBar(new Rect(searchX, headerY, searchW, headerH));
            }
        }

        Text.Font = GameFont.Small;

        // Content Area Box
        Rect boxRect = new Rect(rect.x, rect.y + headerH + 2f, rect.width, rect.height - headerH - 2f);
        Widgets.DrawBoxSolid(boxRect, new Color(0.06f, 0.06f, 0.07f, 0.9f));
        Color prevCol = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, 0.12f);
        Widgets.DrawBox(boxRect);
        GUI.color = prevCol;

        float innerWidth = boxRect.width - 16f;
        _lastRequestWidth = innerWidth;
        string currentText = _requestUnescapeNewlines ? _requestUnescaped : _requestRaw;
        string displayText = _requestEditMode ? currentText : HighlightReport(currentText);

        if (!_requestEditMode && !string.IsNullOrWhiteSpace(_searchText) && _matches.Count > 0)
        {
            displayText = ApplySearchHighlight(displayText, _searchText.Trim());
        }

        GUIStyle activeStyle = _requestEditMode ? _editMonoStyle : _viewMonoStyle;
        float textCalcHeight = activeStyle.CalcHeight(new GUIContent(displayText), innerWidth);
        float contentHeight = Mathf.Max(boxRect.height, textCalcHeight + 20f);

        Rect viewRect = new Rect(0f, 0f, innerWidth, contentHeight);
        Widgets.BeginScrollView(boxRect, ref _requestScrollPos, viewRect);

        Rect textRect = new Rect(4f, 4f, innerWidth - 8f, contentHeight - 8f);
        if (_requestEditMode)
        {
            string newText = GUI.TextArea(textRect, currentText, _editMonoStyle);
            if (newText != currentText)
            {
                ValidateJson(newText);
                if (_requestUnescapeNewlines)
                {
                    _requestUnescaped = newText;
                    try
                    {
                        var parsed = JsonUtil.ParseJsonValue(newText.Trim(), out _);
                        _requestRaw = JsonUtil.SerializeJsonValue(parsed, indent: true);
                    }
                    catch
                    {
                        // Temporary syntax error while typing
                    }
                }
                else
                {
                    _requestRaw = newText;
                    _requestUnescaped = JsonUtil.UnescapeNewlines(newText);
                }
                UpdateSearchMatches(scrollToMatch: false);
            }
        }
        else
        {
            GUI.Label(textRect, displayText, _viewMonoStyle);
        }

        Widgets.EndScrollView();

        if (!_requestEditMode && Event.current.type == EventType.MouseDrag && boxRect.Contains(Event.current.mousePosition))
        {
            Event.current.Use();
        }
    }

    private void DrawResponseSection(Rect rect)
    {
        float headerH = 24f;
        float headerY = rect.y;
        bool isGenerating = IsResponseGenerating;

        // Header Title
        Text.Font = GameFont.Tiny;
        GUI.color = new Color(0.6f, 0.95f, 0.65f);
        Widgets.Label(new Rect(rect.x, headerY + 2f, 190f, headerH), "--- RESPONSE PAYLOAD (JSON) ---");
        GUI.color = Color.white;

        // HTTP Status Code
        Payload p = CurrentPayload;
        if (p?.StatusCode != null && p.StatusCode.Value > 0)
        {
            int code = p.StatusCode.Value;
            Color statusColor = (code >= 200 && code < 300)
                ? new Color(0.4f, 0.85f, 0.4f)
                : new Color(1f, 0.45f, 0.45f);
            GUI.color = statusColor;
            Widgets.Label(new Rect(rect.x + 195f, headerY + 2f, 120f, headerH), $"[HTTP {code}]");
            GUI.color = Color.white;
        }

        // Header Controls (Right-aligned)
        float curX = rect.xMax;

        // Copy button (icon)
        float iconSize = 20f;
        curX -= iconSize;
        Rect copyRect = new Rect(curX, headerY + 2f, iconSize, iconSize);
        bool canCopy = !isGenerating && !string.IsNullOrEmpty(_responseRaw);
        GUI.color = canCopy ? Color.white : new Color(1f, 1f, 1f, 0.35f);
        if (Widgets.ButtonImage(copyRect, TexButton.Copy) && canCopy)
        {
            GUIUtility.systemCopyBuffer = _responseRaw;
            Messages.Message("RimTalk.DebugWindow.Copied".Translate(), MessageTypeDefOf.TaskCompletion, false);
        }
        GUI.color = Color.white;
        TooltipHandler.TipRegion(copyRect, "RimTalk.DebugWindow.CopyClipboardTooltip".Translate());

        Text.Font = GameFont.Small;

        // Content Area Box
        Rect boxRect = new Rect(rect.x, rect.y + headerH + 2f, rect.width, rect.height - headerH - 2f);
        Widgets.DrawBoxSolid(boxRect, new Color(0.06f, 0.06f, 0.07f, 0.9f));
        Color prevCol = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, 0.12f);
        Widgets.DrawBox(boxRect);
        GUI.color = prevCol;

        if (isGenerating)
        {
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            Widgets.Label(boxRect, $"[{"RimTalk.DebugWindow.Generating".Translate()}]");
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            return;
        }

        float innerWidth = boxRect.width - 16f;
        _lastResponseWidth = innerWidth;
        string displayText = HighlightReport(_responseRaw);

        if (!string.IsNullOrWhiteSpace(_searchText) && _matches.Count > 0)
        {
            displayText = ApplySearchHighlight(displayText, _searchText.Trim());
        }

        float textCalcHeight = _viewMonoStyle.CalcHeight(new GUIContent(displayText), innerWidth);
        float contentHeight = Mathf.Max(boxRect.height, textCalcHeight + 20f);

        Rect viewRect = new Rect(0f, 0f, innerWidth, contentHeight);
        Widgets.BeginScrollView(boxRect, ref _responseScrollPos, viewRect);

        Rect textRect = new Rect(4f, 4f, innerWidth - 8f, contentHeight - 8f);
        GUI.Label(textRect, displayText, _viewMonoStyle);

        Widgets.EndScrollView();

        if (Event.current.type == EventType.MouseDrag && boxRect.Contains(Event.current.mousePosition))
        {
            Event.current.Use();
        }
    }

    private string GetEffectiveRequestJson()
    {
        if (_requestUnescapeNewlines)
        {
            try
            {
                var parsed = JsonUtil.ParseJsonValue(_requestUnescaped.Trim(), out _);
                _requestRaw = JsonUtil.SerializeJsonValue(parsed, indent: true);
            }
            catch
            {
                return _requestUnescaped;
            }
        }
        return _requestRaw;
    }

    private void ExecuteResend()
    {
        if (_requestEditMode && !_isJsonValid)
            return;

        // 1. Get edited request string (always serialized to valid JSON)
        string currentEditedRequest = GetEffectiveRequestJson();

        // 2. If custom callback is provided, invoke it with the edited request
        if (_onResendCustomRequest != null)
        {
            _onResendCustomRequest.Invoke(currentEditedRequest);
            return;
        }

        // 3. Dispatch resend with raw JSON override if edited
        if (_apiLog?.TalkRequest != null)
        {
            TalkRequest debugRequest = _apiLog.TalkRequest.Clone();
            bool wasEdited = !string.Equals(currentEditedRequest, _initialRequestRaw, StringComparison.Ordinal);

            if (wasEdited)
            {
                debugRequest.RawJsonOverride = currentEditedRequest;
            }

            _trackedResendRequest = debugRequest;
            _responseRaw = string.Empty;
            _lastLoadedResponse = null;

            ResendTalkRequest(debugRequest, _apiLog.Channel);
            return;
        }

        // 4. Fallback to existing onResend callback
        _onResend?.Invoke();
    }

    private static void ResendTalkRequest(TalkRequest debugRequest, Channel channel)
    {
        if (AIService.IsBusy())
        {
            AIService.CancelCurrent();
        }

        foreach (var pawnState in Cache.GetAll())
        {
            pawnState.IgnoreAllTalkResponses();
        }
        SpeechBubbleDrawer.Clear();

        Task.Run(async () =>
        {
            int waited = 0;
            while (AIService.IsBusy() && waited < 2000)
            {
                await Task.Delay(30);
                waited += 30;
            }

            if (channel == Channel.Stream)
                TalkService.GenerateTalkDebug(debugRequest);
            else if (channel == Channel.Query)
                _ = AIService.Query<PersonalityData>(debugRequest);
        });

        Messages.Message("RimTalk.DebugWindow.ResendSuccess".Translate(), MessageTypeDefOf.TaskCompletion);
    }

    private void InitStyles()
    {
        if (_viewMonoStyle == null)
        {
            _viewMonoStyle = new GUIStyle(Text.fontStyles[(int)GameFont.Tiny])
            {
                fontSize = 12,
                wordWrap = true,
                richText = true, // View mode uses richText for colors
                alignment = TextAnchor.UpperLeft,
                normal = { textColor = new Color(0.92f, 0.92f, 0.92f) }
            };
        }

        if (_editMonoStyle == null)
        {
            _editMonoStyle = new GUIStyle(Text.fontStyles[(int)GameFont.Tiny])
            {
                fontSize = 12,
                wordWrap = true,
                richText = false, // Critical: richText MUST be false for accurate cursor positioning & clicking
                alignment = TextAnchor.UpperLeft,
                normal = { textColor = new Color(0.92f, 0.92f, 0.92f) }
            };
        }
    }

    /// <summary>
    /// Lightweight syntax and markdown highlighter for human-readable API report view.
    /// </summary>
    private static string HighlightReport(string text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;

        // JSON keys ("key":)
        text = Regex.Replace(text, @"""([A-Za-z0-9_]+)""\s*:", "<color=#79c0ff>\"$1\"</color>:");

        // JSON booleans and null
        text = Regex.Replace(text, @":\s*(true|false|null)\b", ": <color=#c678dd>$1</color>");

        // Report and payload dividers (e.g. === PAYLOAD === or --- CHAT ---)
        text = Regex.Replace(text, @"((?:^|\\n|\r?\n)(?:={3,}[^=\r\n]+={3,}|-{3,}[^-\r\n]+-{3,}))(?=$|\\n|\r?\n)", "<color=#61afef><b>$1</b></color>");

        // Markdown headings (#, ##, ###)
        text = Regex.Replace(text, @"((?:^|\\n|\r?\n)#{1,3}\s+[^\r\n""\\]+)(?=$|\\n|\r?\n)", "<color=#4ec9b0><b>$1</b></color>");

        // Section tags like [Environment], [P1], [P2]
        text = Regex.Replace(text, @"\[[^\]\r\n\\]+\]", "<color=#e5c07b><b>$0</b></color>");
        
        // Key attributes at line start (e.g. Time:, Season:, Location:, Skills:, Role:)
        text = Regex.Replace(text, @"((?:^|\\n|\r?\n)[A-Z][A-Za-z0-9_\- ]{1,25}:)(?!\/\/)", "<color=#56b6c2>$1</color>");

        // Highlight escaped newlines (\n, \r\n) in raw view (orange/amber)
        text = Regex.Replace(text, @"(\\(?:r\\n|n|r))", "<color=#d19a66><b>$1</b></color>");

        return text;
    }

    private static bool DrawToggle(Rect rect, string label, ref bool val, string tooltip = null)
    {
        float boxSize = 20f;
        Rect boxRect = new Rect(rect.x, rect.y + (rect.height - boxSize) * 0.5f, boxSize, boxSize);
        Rect labelRect = new Rect(boxRect.xMax + 5f, rect.y + 2f, rect.width - boxSize - 5f, rect.height);

        bool prev = val;
        Widgets.Checkbox(new Vector2(boxRect.x, boxRect.y), ref val, boxSize);

        if (Widgets.ButtonInvisible(labelRect))
        {
            val = !val;
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        Text.Font = GameFont.Tiny;
        Widgets.Label(labelRect, label);
        if (!string.IsNullOrEmpty(tooltip)) TooltipHandler.TipRegion(rect, tooltip);
        return val != prev;
    }

    private void DrawSearchBar(Rect rect)
    {
        bool hasSearch = !string.IsNullOrWhiteSpace(_searchText);
        string countLabel = _matches.Count > 0 ? $"{_currentMatchIndex + 1}/{_matches.Count}" : "0";
        Text.Font = GameFont.Tiny;
        float countW = hasSearch ? Mathf.Max(36f, Text.CalcSize(countLabel).x + 8f) : 0f;
        float arrowBtnW = 18f;
        float navW = hasSearch ? countW + (arrowBtnW * 2f) + 6f : 0f;

        float inputW = rect.width - navW;
        Rect inputRect = new Rect(rect.x, rect.y, Mathf.Max(50f, inputW), rect.height);

        string newSearch = Widgets.TextField(inputRect, _searchText);
        if (string.IsNullOrEmpty(newSearch))
        {
            var prevCol = GUI.color;
            GUI.color = new Color(0.6f, 0.6f, 0.6f, 0.7f);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(inputRect.x + 4f, inputRect.y, inputRect.width - 4f, inputRect.height), "RimTalk.DebugWindow.Search".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = prevCol;
        }

        if (newSearch != _searchText)
        {
            _searchText = newSearch;
            UpdateSearchMatches(scrollToMatch: true);
        }

        if (hasSearch)
        {
            float curX = inputRect.xMax + 2f;
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Tiny;
            GUI.color = _matches.Count > 0 ? Color.white : new Color(1f, 0.45f, 0.45f);
            Widgets.Label(new Rect(curX, rect.y, countW, rect.height), countLabel);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;

            curX += countW + 2f;
            GUI.enabled = _matches.Count > 0;
            if (Widgets.ButtonText(new Rect(curX, rect.y, arrowBtnW, rect.height), "▲")) PrevMatch();
            if (Widgets.ButtonText(new Rect(curX + arrowBtnW + 2f, rect.y, arrowBtnW, rect.height), "▼")) NextMatch();
            GUI.enabled = true;
        }
    }

    private void UpdateSearchMatches(bool scrollToMatch = true)
    {
        _matches.Clear();
        _currentMatchIndex = -1;
        string needle = _searchText?.Trim();
        if (string.IsNullOrEmpty(needle)) return;

        // 1. Search Request (only in view mode)
        if (!_requestEditMode)
        {
            string reqText = _requestUnescapeNewlines ? _requestUnescaped : _requestRaw;
            if (!string.IsNullOrEmpty(reqText))
            {
                for (int idx = 0; (idx = reqText.IndexOf(needle, idx, StringComparison.OrdinalIgnoreCase)) >= 0; idx += needle.Length)
                    _matches.Add(new SearchMatch { IsRequest = true, CharIndex = idx });
            }
        }

        // 2. Search Response
        if (!string.IsNullOrEmpty(_responseRaw))
        {
            for (int idx = 0; (idx = _responseRaw.IndexOf(needle, idx, StringComparison.OrdinalIgnoreCase)) >= 0; idx += needle.Length)
                _matches.Add(new SearchMatch { IsRequest = false, CharIndex = idx });
        }

        if (_matches.Count > 0)
        {
            _currentMatchIndex = 0;
            if (scrollToMatch) ScrollToCurrentMatch();
        }
    }

    private void NextMatch()
    {
        if (_matches.Count == 0) return;
        _currentMatchIndex = (_currentMatchIndex + 1) % _matches.Count;
        ScrollToCurrentMatch();
    }

    private void PrevMatch()
    {
        if (_matches.Count == 0) return;
        _currentMatchIndex = (_currentMatchIndex - 1 + _matches.Count) % _matches.Count;
        ScrollToCurrentMatch();
    }

    private void ScrollToCurrentMatch()
    {
        if (_currentMatchIndex < 0 || _currentMatchIndex >= _matches.Count) return;
        var match = _matches[_currentMatchIndex];

        if (match.IsRequest)
        {
            string reqText = _requestUnescapeNewlines ? _requestUnescaped : _requestRaw;
            if (string.IsNullOrEmpty(reqText) || match.CharIndex > reqText.Length) return;

            float matchY = _viewMonoStyle?.CalcHeight(new GUIContent(reqText.Substring(0, match.CharIndex)), _lastRequestWidth) ?? 0f;
            _requestScrollPos.y = Mathf.Max(0f, matchY - 60f);
        }
        else if (!string.IsNullOrEmpty(_responseRaw) && match.CharIndex <= _responseRaw.Length)
        {
            float matchY = _viewMonoStyle?.CalcHeight(new GUIContent(_responseRaw.Substring(0, match.CharIndex)), _lastResponseWidth) ?? 0f;
            _responseScrollPos.y = Mathf.Max(0f, matchY - 40f);
        }
    }

    private static string ApplySearchHighlight(string text, string needle)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(needle)) return text ?? string.Empty;
        return Regex.Replace(text, @"</?[^>]+>|(" + Regex.Escape(needle) + ")",
            m => m.Groups[1].Success ? $"<color=#FFE066><b>{m.Value}</b></color>" : m.Value,
            RegexOptions.IgnoreCase);
    }

    private const string SteamDiscussionUrl = "https://steamcommunity.com/workshop/filedetails/discussion/3551203752/690871474424385068/";
    private bool _isUploadingReport;

    private void ConfirmAndReportIssue()
    {
        Find.WindowStack.Add(new Dialog_ReportConfirmation(this));
    }

    private sealed class Dialog_ReportConfirmation : Window
    {
        private readonly Dialog_ApiLog _parent;
        private Vector2 _scrollPos;

        public override Vector2 InitialSize => new Vector2(650f, 520f);

        public Dialog_ReportConfirmation(Dialog_ApiLog parent)
        {
            _parent = parent;
            forcePause = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
            closeOnAccept = false;
            closeOnCancel = true;
            doCloseX = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            float buttonH = 38f;
            float buttonGap = 10f;
            float buttonsY = inRect.height - buttonH;

            Rect textOutRect = new Rect(0f, 0f, inRect.width, buttonsY - 12f);
            string message = "RimTalk.DebugWindow.ReportConfirmation".Translate();

            Text.Font = GameFont.Small;
            float textH = Text.CalcHeight(message, textOutRect.width - 16f);
            Rect textInRect = new Rect(0f, 0f, textOutRect.width - 16f, Mathf.Max(textH, textOutRect.height));

            Widgets.BeginScrollView(textOutRect, ref _scrollPos, new Rect(0f, 0f, textOutRect.width - 16f, textH));
            Widgets.Label(textInRect, message);
            Widgets.EndScrollView();

            // 3 Buttons: [Upload Log] [Open Steam Discussion] [Cancel]
            int buttonCount = 3;
            float totalGap = buttonGap * (buttonCount - 1);
            float btnW = (inRect.width - totalGap) / buttonCount;

            // 1. Upload Log
            Rect btn1Rect = new Rect(0f, buttonsY, btnW, buttonH);
            if (_parent._isUploadingReport)
            {
                GUI.enabled = false;
            }
            if (Widgets.ButtonText(btn1Rect, "RimTalk.DebugWindow.UploadLog".Translate()))
            {
                _parent.StartReportUpload();
            }
            GUI.enabled = true;

            // 2. Open Steam Discussion
            Rect btn2Rect = new Rect(btnW + buttonGap, buttonsY, btnW, buttonH);
            if (Widgets.ButtonText(btn2Rect, "RimTalk.DebugWindow.OpenSteamDiscussion".Translate()))
            {
                Application.OpenURL(SteamDiscussionUrl);
            }

            // 3. Cancel / Close
            Rect btn3Rect = new Rect((btnW + buttonGap) * 2f, buttonsY, btnW, buttonH);
            if (Widgets.ButtonText(btn3Rect, "Cancel".Translate()))
            {
                Close();
            }
        }
    }

    private void StartReportUpload()
    {
        if (_isUploadingReport) return;
        _isUploadingReport = true;

        string reportText = BuildCompleteReport();
        Messages.Message("RimTalk.DebugWindow.ReportUploading".Translate(), MessageTypeDefOf.NeutralEvent, false);

        Task.Run(() =>
        {
            string pasteUrl = null;
            try
            {
                using var client = new WebClient();
                client.Headers[HttpRequestHeader.UserAgent] = "RimTalk-Mod/1.0";
                var values = new NameValueCollection
                {
                    { "content", reportText },
                    { "expiry_days", "30" }
                };

                byte[] responseBytes = client.UploadValues("https://dpaste.com/api/", "POST", values);
                pasteUrl = Encoding.UTF8.GetString(responseBytes)?.Trim();
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to upload report: {ex.Message}");
            }

            _isUploadingReport = false;

            if (!string.IsNullOrEmpty(pasteUrl) && pasteUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                GUIUtility.systemCopyBuffer = pasteUrl;
                Messages.Message("RimTalk.DebugWindow.ReportSuccess".Translate(), MessageTypeDefOf.TaskCompletion, false);
            }
            else
            {
                GUIUtility.systemCopyBuffer = reportText;
                Messages.Message("RimTalk.DebugWindow.ReportFailed".Translate(), MessageTypeDefOf.CautionInput, false);
            }
        });
    }

    private string BuildCompleteReport()
    {
        Payload p = CurrentPayload;
        var sb = new StringBuilder();
        sb.AppendLine("=== RIMTALK API REPORT ===");
        sb.AppendLine($"URL:      {p?.URL ?? "-"}");
        sb.AppendLine($"Model:    {p?.Model ?? "-"}");
        if (p?.StatusCode != null && p.StatusCode.Value > 0)
            sb.AppendLine($"Status:   HTTP {p.StatusCode.Value}");
        string tokenStr = (p?.TokenCount ?? 0) > 0 ? p.TokenCount.ToString() : (IsResponseGenerating ? "-" : "0");
        sb.AppendLine($"Tokens:   {tokenStr}");
        if (!string.IsNullOrEmpty(p?.ErrorMessage))
            sb.AppendLine($"Error:    {p.ErrorMessage}");
        sb.AppendLine();
        sb.AppendLine("--- REQUEST PAYLOAD ---");
        string reqStr = GetEffectiveRequestJson();
        sb.AppendLine(string.IsNullOrEmpty(reqStr) ? "EMPTY" : reqStr);
        sb.AppendLine();
        sb.AppendLine("--- RESPONSE PAYLOAD ---");
        if (IsResponseGenerating)
            sb.AppendLine($"[{"RimTalk.DebugWindow.Generating".Translate()}]");
        else
            sb.AppendLine(string.IsNullOrEmpty(_responseRaw) ? "EMPTY" : _responseRaw);
        sb.AppendLine("==========================");
        return sb.ToString();
    }
}

