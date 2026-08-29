using System;

namespace RimTalk.Service;

// Backstop for AIService's busy flag: if it's been held longer than any request could
// legitimately take, treat it as stuck and release it rather than staying silent forever.
public static class BusyGate
{
    // Client allows 60s connect + 60s read inactivity, plus retries on top, so a slow
    // model on a bad connection can take minutes. 300s is well past that.
    public const int StuckAfterSeconds = 300;

    public static bool IsStuck(bool busy, DateTime? busySince, DateTime now,
                               int stuckAfterSeconds = StuckAfterSeconds)
    {
        if (!busy) return false;

        // No stamp means nobody recorded the start; don't clear a request we know nothing about.
        if (busySince == null) return false;

        return (now - busySince.Value).TotalSeconds >= stuckAfterSeconds;
    }
}
