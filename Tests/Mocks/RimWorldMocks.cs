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
}

namespace RimWorld
{
}
