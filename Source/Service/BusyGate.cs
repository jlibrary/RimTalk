using System;

namespace RimTalk.Service;

// Backstop for AIService's busy flag: if it's been held longer than any request could
// legitimately take, treat it as stuck and release it rather than staying silent forever.
public static class BusyGate
{
    // Default client timeout is 60s connect + 60s read inactivity, plus one retry.
    // 300s is past that. A higher user timeout raises the backstop in StuckAfter.
    public const int StuckAfterSeconds = 300;

    // Connect wait plus one read stall, twice, for the single retry. Never shorter than
    // the 300s floor, so the default 60s timeout still frees the slot on the old schedule.
    public static int StuckAfter(int requestTimeoutSeconds)
    {
        int budget = requestTimeoutSeconds * 4;
        return budget > StuckAfterSeconds ? budget : StuckAfterSeconds;
    }

    public static bool IsStuck(bool busy, DateTime? busySince, DateTime now,
                               int stuckAfterSeconds = StuckAfterSeconds)
    {
        if (!busy) return false;

        // No stamp means nobody recorded the start; don't clear a request we know nothing about.
        if (busySince == null) return false;

        return (now - busySince.Value).TotalSeconds >= stuckAfterSeconds;
    }
}
