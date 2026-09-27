using System;
using System.Collections.Generic;

namespace Verse
{
    public class Pawn
    {
        public string Name { get; set; } = "MockColonist";
        public string LabelShort => Name;
    }

    public class Map
    {
        public int uniqueID { get; set; } = 1;
    }

    public interface IExposable
    {
        void ExposeData();
    }

    public enum LookMode { Value, Deep, Reference }
    public enum LoadSaveMode { Inactive, LoadingVars, Saving, PostLoadInit }

    public static class Scribe
    {
        public static LoadSaveMode mode = LoadSaveMode.Inactive;
    }

    public static class GenFilePaths
    {
        public static string ConfigFolderPath => System.IO.Path.GetTempPath();
    }

    public static class Scribe_Values
    {
        public static void Look<T>(ref T value, string label, T defaultValue = default, bool forceSave = false) { }
    }

    public static class Scribe_Collections
    {
        public static void Look<T>(ref List<T> list, string label, LookMode lookMode = LookMode.Value, params object[] ctorArgs) { }
        public static void Look<TKey, TValue>(ref Dictionary<TKey, TValue> dict, string label, LookMode keyLookMode = LookMode.Value, LookMode valueLookMode = LookMode.Value) { }
    }

    public static class ModsConfig
    {
        public static Func<string, bool> IsActiveHandler;
        public static bool IsActive(string mod) => IsActiveHandler?.Invoke(mod) ?? false;
    }
}

namespace RimWorld
{
}

namespace UnityEngine
{
    public static class Mathf
    {
        public static float Clamp(float value, float min, float max) => Math.Clamp(value, min, max);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static int Max(int a, int b) => Math.Max(a, b);
        public static float Min(float a, float b) => Math.Min(a, b);
        public static int Min(int a, int b) => Math.Min(a, b);
        public static int FloorToInt(float f) => (int)Math.Floor(f);
        public static float Pow(float f, float p) => (float)Math.Pow(f, p);
        public static float Abs(float f) => Math.Abs(f);
    }
}
