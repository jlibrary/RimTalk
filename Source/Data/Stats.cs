using System;
using System.Collections.Generic;

namespace RimTalk.Data;

public static class Stats
{
    // Cumulative totals
    public static long TotalTokens { get; private set; }
    public static long TotalCalls { get; private set; }
    public static DateTime StartTime { get; private set; }

    // Per-minute counters
    private static long _tokensThisMinute;
    private static long _callsThisMinute;

    // Per-second counters
    private static long _tokensThisSecond;
    private static long _callsThisSecond;
    private static long _currentRequestHeight;
    private static int _activeRequestPoints;
    private static long _pendingRequestHeightThisSecond;
    private static long _pendingRequestTokensThisSecond;
    private static bool _justCompletedWithActivePoints;

    public static void AdjustActiveRequestPoints(long actualTokens)
    {
        if (actualTokens <= 0) return;
        _currentRequestHeight = actualTokens;
        if (_activeRequestPoints > 0)
        {
            int startIdx = Math.Max(0, TokensPerSecondHistory.Count - _activeRequestPoints);
            for (int i = startIdx; i < TokensPerSecondHistory.Count; i++)
            {
                TokensPerSecondHistory[i] = actualTokens;
                if (i < TokenLabels.Count) TokenLabels[i] = actualTokens;
            }
            _justCompletedWithActivePoints = true;
            _activeRequestPoints = 0;
        }
        else
        {
            _pendingRequestHeightThisSecond = actualTokens;
            _pendingRequestTokensThisSecond = actualTokens;
        }

        // Scale prior fallback points (height 1 without tokens) so they don't collapse when real tokens arrive
        for (int i = 0; i < TokensPerSecondHistory.Count; i++)
        {
            if (TokensPerSecondHistory[i] == 1 && (i >= TokenLabels.Count || TokenLabels[i] == 0))
            {
                TokensPerSecondHistory[i] = actualTokens;
            }
        }
    }

    // Per-minute historical data (last 60 minutes)
    public static readonly List<long> TokensPerMinuteHistory = [];
    public static readonly List<long> CallsPerMinuteHistory = [];
    public static readonly List<long> AvgTokensPerCallHistory = [];

    // Per-second historical data (last 60 seconds)
    public static readonly List<long> TokensPerSecondHistory = [];
    // Parallel to TokensPerSecondHistory: real token count for labels (0 = no label)
    public static readonly List<long> TokenLabels = [];
    public static readonly List<long> AvgTokensPerCallPerSecondHistory = [];

    private static DateTime _nextMinuteRolloverTime;
    private static DateTime _nextSecondRolloverTime;

    // Lifetime averages
    public static double AvgCallsPerMinute { get; private set; }
    public static double AvgTokensPerMinute { get; private set; }
    public static double AvgTokensPerCall { get; private set; }

    static Stats()
    {
        Reset();
    }

    public static void IncrementTokens(long amount)
    {
        TotalTokens += amount;
        _tokensThisMinute += amount;
        _tokensThisSecond += amount;
    }

    public static void IncrementCalls()
    {
        TotalCalls++;
        _callsThisMinute++;
        _callsThisSecond++;
    }

    private static long GetHistoryMax()
    {
        long max = 0;
        for (int i = 0; i < TokensPerSecondHistory.Count; i++)
        {
            if (TokensPerSecondHistory[i] > max) max = TokensPerSecondHistory[i];
        }
        return max > 0 ? max : 1;
    }

    public static void Update()
    {
        double elapsedMinutes = (DateTime.Now - StartTime).TotalMinutes;
        if (elapsedMinutes > 0)
        {
            AvgCallsPerMinute = TotalCalls / elapsedMinutes;
            AvgTokensPerMinute = TotalTokens / elapsedMinutes;
            AvgTokensPerCall = TotalCalls > 0 ? (double)TotalTokens / TotalCalls : 0;
        }

        // --- Handle per-second rollover ---
        if (DateTime.Now >= _nextSecondRolloverTime)
        {
            bool isBusy = Service.AIService.IsBusy();
            long valToRecord;
            long labelToRecord = 0;

            if (isBusy)
            {
                if (_currentRequestHeight <= 0)
                    _currentRequestHeight = GetHistoryMax();
                valToRecord = _currentRequestHeight;
                _activeRequestPoints++;
            }
            else
            {
                if (_pendingRequestHeightThisSecond > 0)
                {
                    valToRecord = _pendingRequestHeightThisSecond;
                    labelToRecord = _pendingRequestTokensThisSecond;
                    _pendingRequestHeightThisSecond = 0;
                    _pendingRequestTokensThisSecond = 0;
                }
                else if (!_justCompletedWithActivePoints && _activeRequestPoints == 0 && _callsThisSecond > 0)
                {
                    valToRecord = GetHistoryMax();
                    labelToRecord = 0;
                }
                else
                {
                    valToRecord = 0;
                    labelToRecord = 0;
                }
                _currentRequestHeight = 0;
                _activeRequestPoints = 0;
                _justCompletedWithActivePoints = false;
            }

            TokensPerSecondHistory.Add(valToRecord);
            TokenLabels.Add(labelToRecord);
            long avgForLastSecond = _callsThisSecond > 0 ? _tokensThisSecond / _callsThisSecond : 0;
            AvgTokensPerCallPerSecondHistory.Add(avgForLastSecond);

            _tokensThisSecond = 0;
            _callsThisSecond = 0;
            _nextSecondRolloverTime = _nextSecondRolloverTime.AddSeconds(1);

            while (_nextSecondRolloverTime < DateTime.Now)
            {
                long catchup = isBusy ? _currentRequestHeight : 0;
                TokensPerSecondHistory.Add(catchup);
                TokenLabels.Add(0);
                AvgTokensPerCallPerSecondHistory.Add(0);
                _nextSecondRolloverTime = _nextSecondRolloverTime.AddSeconds(1);
            }

            while (TokensPerSecondHistory.Count > 60) { TokensPerSecondHistory.RemoveAt(0); TokenLabels.RemoveAt(0); }
            while (AvgTokensPerCallPerSecondHistory.Count > 60) AvgTokensPerCallPerSecondHistory.RemoveAt(0);
        }

        // --- Handle per-minute rollover ---
        if (DateTime.Now < _nextMinuteRolloverTime) return;

        TokensPerMinuteHistory.Add(_tokensThisMinute);
        CallsPerMinuteHistory.Add(_callsThisMinute);
        long avgForLastMinute = _callsThisMinute > 0 ? _tokensThisMinute / _callsThisMinute : 0;
        AvgTokensPerCallHistory.Add(avgForLastMinute);

        while (TokensPerMinuteHistory.Count > 60) TokensPerMinuteHistory.RemoveAt(0);
        while (CallsPerMinuteHistory.Count > 60) CallsPerMinuteHistory.RemoveAt(0);
        while (AvgTokensPerCallHistory.Count > 60) AvgTokensPerCallHistory.RemoveAt(0);

        _tokensThisMinute = 0;
        _callsThisMinute = 0;
        _nextMinuteRolloverTime = _nextMinuteRolloverTime.AddMinutes(1);

        while (_nextMinuteRolloverTime < DateTime.Now)
        {
            TokensPerMinuteHistory.Add(0);
            CallsPerMinuteHistory.Add(0);
            AvgTokensPerCallHistory.Add(0);
            _nextMinuteRolloverTime = _nextMinuteRolloverTime.AddMinutes(1);
        }
    }

    public static void Reset()
    {
        TotalTokens = 0;
        TotalCalls = 0;
        StartTime = DateTime.Now;
        _tokensThisMinute = 0;
        _callsThisMinute = 0;
        _tokensThisSecond = 0;
        _callsThisSecond = 0;

        TokensPerMinuteHistory.Clear();
        CallsPerMinuteHistory.Clear();
        AvgTokensPerCallHistory.Clear();
        TokensPerSecondHistory.Clear();
        TokenLabels.Clear();
        AvgTokensPerCallPerSecondHistory.Clear();

        _nextMinuteRolloverTime = DateTime.Now.AddMinutes(1);
        _nextSecondRolloverTime = DateTime.Now.AddSeconds(1);
        AvgCallsPerMinute = 0;
        AvgTokensPerMinute = 0;
        AvgTokensPerCall = 0;
        _currentRequestHeight = 0;
        _activeRequestPoints = 0;
        _pendingRequestHeightThisSecond = 0;
        _pendingRequestTokensThisSecond = 0;
        _justCompletedWithActivePoints = false;
    }
}