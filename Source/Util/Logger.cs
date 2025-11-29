using Verse;

namespace RimTalk.Util;

public static class Logger
{
    private const string ModTag = "[RimTalk]";
    public static void Message(string message)
    {
        Log.Message($"{ModTag} {message}\n\n");
    }
        
    public static void Debug(string message)
    {
        if (Prefs.LogVerbose)
            Log.Message($"{ModTag} {message}\n\n");
    }
        
    public static void Warning(string message)
    {
        Log.Warning($"{ModTag} {message}\n\n");
    }
        
    public static void Error(string message)
    {
        Log.Error($"{ModTag} {message}\n\n");
    }

    public static void ErrorOnce(string text, int key)
    {
        Log.ErrorOnce($"{ModTag} {text}\n\n", key);
    }
}