using RimTalk.Data;
using Verse;

namespace RimTalk.Util
{
    public static class Logger
    {
        public static void Message(string message)
        {
            Log.Message($"{Constant.ModTag} {message}\n\n");
        }
        
        public static void Debug(string message)
        {
            if (Prefs.LogVerbose)
                Log.Message($"{Constant.ModTag} {message}\n\n");
        }
        
        public static void Warning(string message)
        {
            Log.Warning($"{Constant.ModTag} {message}\n\n");
        }
        
        public static void Error(string message)
        {
            Log.Error($"{Constant.ModTag} {message}\n\n");
        }
    }
}