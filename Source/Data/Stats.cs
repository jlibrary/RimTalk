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

    // Per-minute historical data (last 60 minutes)
    public static readonly List<long> TokensPerMinuteHistory = [];
    public static readonly List<long> CallsPerMinuteHistory = [];
    public static readonly List<long> AvgTokensPerCallHistory = [];

    // Per-second historical data (last 60 seconds)
    public static readonly List<long> TokensPerSecondHistory = [];
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
            TokensPerSecondHistory.Add(_tokensThisSecond);
            long avgForLastSecond = _callsThisSecond > 0 ? _tokensThisSecond / _callsThisSecond : 0;
            AvgTokensPerCallPerSecondHistory.Add(avgForLastSecond);

            while (TokensPerSecondHistory.Count > 60) TokensPerSecondHistory.RemoveAt(0);
            while (AvgTokensPerCallPerSecondHistory.Count > 60) AvgTokensPerCallPerSecondHistory.RemoveAt(0);

            _tokensThisSecond = 0;
            _callsThisSecond = 0;
            _nextSecondRolloverTime = _nextSecondRolloverTime.AddSeconds(1);

            while (_nextSecondRolloverTime < DateTime.Now)
            {
                TokensPerSecondHistory.Add(0);
                AvgTokensPerCallPerSecondHistory.Add(0);
                _nextSecondRolloverTime = _nextSecondRolloverTime.AddSeconds(1);
            }
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
        AvgTokensPerCallPerSecondHistory.Clear();

        _nextMinuteRolloverTime = DateTime.Now.AddMinutes(1);
        _nextSecondRolloverTime = DateTime.Now.AddSeconds(1);
        AvgCallsPerMinute = 0;
        AvgTokensPerMinute = 0;
        AvgTokensPerCall = 0;
    }
}