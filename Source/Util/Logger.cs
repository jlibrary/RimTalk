using Verse;

namespace RimTalk.Util
{
    public static class Logger
    {
        private const string LogName = "RimTalk";
        public static void Message(string message)
        {
            Log.Message($"[{LogName}] {message}");
        }
        
        public static void Warning(string message)
        {
            Log.Warning($"[{LogName}] {message}");
        }
        
        public static void Error(string message)
        {
            Log.Error($"[{LogName}] {message}");
        }
    }
}