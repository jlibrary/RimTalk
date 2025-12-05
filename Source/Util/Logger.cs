using Verse;

namespace RimTalk.Util;

public static class Logger
{
    private const string ModTag = "[RimTalk]";
    public static void Message(object message)
    {
        Log.Message($"{ModTag} {message}\n\n");
    }
        
    public static void Debug(object message)
    {
        if (Prefs.LogVerbose)
            Log.Message($"{ModTag} {message}\n\n");
    }
        
    public static void Warning(object message)
    {
        Log.Warning($"{ModTag} {message}\n\n");
    }
        
    public static void Error(object message)
    {
        Log.Error($"{ModTag} {message}\n\n");
    }

    public static void ErrorOnce(object text, int key)
    {
        Log.ErrorOnce($"{ModTag} {text}\n\n", key);
    }
}