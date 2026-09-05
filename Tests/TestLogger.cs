using System;

namespace RimTalk.Util;

public static class Logger
{
    public static void Message(object message) => Console.WriteLine($"[INFO] {message}");
    public static void Warning(object message) => Console.WriteLine($"[WARN] {message}");
    public static void Error(object message) => Console.Error.WriteLine($"[ERR] {message}");
    public static void Debug(object message) => Console.WriteLine($"[DBG] {message}");
}
