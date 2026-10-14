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
            if (UIUtil.ButtonText(new Rect(curX, rect.y, arrowBtnW, rect.height), "▲")) PrevMatch();
            if (UIUtil.ButtonText(new Rect(curX + arrowBtnW + 2f, rect.y, arrowBtnW, rect.height), "▼")) NextMatch();
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
}
