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

public partial class Dialog_ApiLog
{
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
            if (UIUtil.ButtonText(btn1Rect, "RimTalk.DebugWindow.UploadLog".Translate()))
            {
                _parent.StartReportUpload();
            }
            GUI.enabled = true;

            // 2. Open Steam Discussion
            Rect btn2Rect = new Rect(btnW + buttonGap, buttonsY, btnW, buttonH);
            if (UIUtil.ButtonText(btn2Rect, "RimTalk.DebugWindow.OpenSteamDiscussion".Translate()))
            {
                Application.OpenURL(SteamDiscussionUrl);
            }

            // 3. Cancel / Close
            Rect btn3Rect = new Rect((btnW + buttonGap) * 2f, buttonsY, btnW, buttonH);
            if (UIUtil.ButtonText(btn3Rect, "Cancel".Translate()))
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
