using System;
using System.IO;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimTalk.Util;

public static class VersionSwitcher
{
    private const string DllName = "RimTalk.dll";
    private const string LastVersionFolder = "LastVersion";

    private static string GetModRootDir() =>
        LoadedModManager.GetMod<Settings>()?.Content?.RootDir ?? "";

    private static string GetLastDllPath()
    {
        string root = GetModRootDir();
        if (string.IsNullOrEmpty(root)) return "";

        string ver = $"{VersionControl.CurrentMajor}.{VersionControl.CurrentMinor}";
        string path = Path.Combine(root, LastVersionFolder, ver, DllName);
        if (File.Exists(path)) return path;

        // Fallback checks
        string flatPath = Path.Combine(root, LastVersionFolder, DllName);
        if (File.Exists(flatPath)) return flatPath;

        string legacyPath = Path.Combine(root, ver, "LastDLL", DllName);
        if (File.Exists(legacyPath)) return legacyPath;

        return "";
    }

    public static bool IsPreviousDllAvailable() => File.Exists(GetLastDllPath());

    public static string GetPreviousVersionString()
    {
        string root = GetModRootDir();
        if (string.IsNullOrEmpty(root)) return null;

        string versionFile = Path.Combine(root, LastVersionFolder, "version.txt");
        if (File.Exists(versionFile))
        {
            string ver = File.ReadAllText(versionFile).Trim();
            if (!string.IsNullOrEmpty(ver))
                return ver.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? ver : "v" + ver;
        }
        return null;
    }

    public static string GetLocalModsFolder()
    {
        var content = LoadedModManager.GetMod<Settings>()?.Content;
        if (content != null && !string.IsNullOrEmpty(content.RootDir))
        {
            string rootDir = content.RootDir.TrimEnd('/', '\\');
            string parent = Path.GetDirectoryName(rootDir);
            if (!string.IsNullOrEmpty(parent) && Path.GetFileName(parent).Equals("Mods", StringComparison.OrdinalIgnoreCase))
            {
                return parent;
            }
        }

        foreach (var mod in ModLister.AllInstalledMods)
        {
            if (!mod.OnSteamWorkshop && !mod.Official && mod.RootDir != null)
            {
                string parent = mod.RootDir.Parent?.FullName;
                if (!string.IsNullOrEmpty(parent) && Path.GetFileName(parent).Equals("Mods", StringComparison.OrdinalIgnoreCase))
                    return parent;
            }
        }

        string currentDir = Directory.GetCurrentDirectory();
        string macMods = Path.Combine(currentDir, "RimWorldMac.app", "Mods");
        if (Directory.Exists(macMods)) return macMods;

        string stdMods = Path.Combine(currentDir, "Mods");
        if (Directory.Exists(stdMods)) return stdMods;

        return stdMods;
    }

    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        foreach (string file in Directory.GetFiles(sourceDir))
        {
            string dest = Path.Combine(targetDir, Path.GetFileName(file));
            File.Copy(file, dest, overwrite: true);
        }
        foreach (string dir in Directory.GetDirectories(sourceDir))
        {
            string destDir = Path.Combine(targetDir, Path.GetFileName(dir));
            CopyDirectory(dir, destDir);
        }
    }

    public static void PromptRollbackConfirmation()
    {
        string prevVer = GetPreviousVersionString() ?? "Previous";

        Find.WindowStack.Add(new Dialog_MessageBox(
            "RimTalk.VersionSwitcher.ConfirmRollbackPrompt".Translate(prevVer).ToString(),
            "RimTalk.VersionSwitcher.ProceedRollback".Translate().ToString(),
            () => ExecuteRollback(prevVer),
            "Cancel".Translate().ToString(),
            title: "RimTalk.VersionSwitcher.ConfirmRollbackTitle".Translate(prevVer).ToString()
        ));
    }

    private static void ExecuteRollback(string prevVer)
    {
        var content = LoadedModManager.GetMod<Settings>()?.Content;
        if (content == null || string.IsNullOrEmpty(content.RootDir)) return;

        string localModsFolder = GetLocalModsFolder();
        string localRimTalk = Path.Combine(localModsFolder, "RimTalk");

        try
        {
            bool isAlreadyLocal = content.RootDir.TrimEnd('/', '\\')
                .Equals(localRimTalk.TrimEnd('/', '\\'), StringComparison.OrdinalIgnoreCase);

            // 1. If running from Workshop, copy the mod into local RimWorld/Mods/RimTalk
            if (!isAlreadyLocal)
            {
                CopyDirectory(content.RootDir, localRimTalk);
            }

            // 2. In the target mod folder, swap 1.5 and 1.6 assemblies with LastVersion
            string targetModDir = isAlreadyLocal ? content.RootDir : localRimTalk;
            string[] versions = new[] { "1.5", "1.6" };
            foreach (string ver in versions)
            {
                string lastDll = Path.Combine(targetModDir, LastVersionFolder, ver, DllName);
                if (!File.Exists(lastDll))
                    lastDll = Path.Combine(targetModDir, LastVersionFolder, DllName);

                string activeDll = Path.Combine(targetModDir, ver, "Assemblies", DllName);

                if (File.Exists(lastDll) && File.Exists(activeDll))
                {
                    File.Copy(lastDll, activeDll, overwrite: true);
                }
            }

            // 3. Update About/About.xml in target folder so RimWorld mod screen displays the correct version
            string aboutXmlPath = Path.Combine(targetModDir, "About", "About.xml");
            if (File.Exists(aboutXmlPath))
            {
                string xml = File.ReadAllText(aboutXmlPath);
                string cleanVer = prevVer.TrimStart('v', 'V');
                xml = System.Text.RegularExpressions.Regex.Replace(xml, @"<modVersion>.*?</modVersion>", $"<modVersion>{cleanVer}</modVersion>");
                File.WriteAllText(aboutXmlPath, xml);
            }

            // 4. Inform the user
            Find.WindowStack.Add(new Dialog_MessageBox(
                "RimTalk.VersionSwitcher.LocalModCreatedPrompt".Translate(prevVer).ToString(),
                "OK".Translate().ToString(),
                title: "RimTalk.VersionSwitcher.RestartRequiredTitle".Translate().ToString()
            ));
        }
        catch (Exception ex)
        {
            Messages.Message($"Failed to create local rollback mod: {ex.Message}", MessageTypeDefOf.RejectInput, false);
            Logger.Error($"[RimTalk] SwitchToPrevious failed: {ex}");
        }
    }

    public static void DrawVersionSwitcher(Listing_Standard listing)
    {
        bool hasLastDll = IsPreviousDllAvailable();
        string prevVer = GetPreviousVersionString();

        listing.Gap(12f);
        Rect rect = listing.GetRect(32f);

        const float btnWidth = 280f;
        Rect labelRect = new Rect(rect.x, rect.y, rect.width - btnWidth - 10f, rect.height);
        Rect btnRect = new Rect(labelRect.xMax + 10f, rect.y, btnWidth, rect.height);

        TextAnchor prevAnchor = Text.Anchor;
        Text.Anchor = TextAnchor.MiddleLeft;
        Widgets.Label(labelRect, "RimTalk.VersionSwitcher.RollbackLabel".Translate().ToString());
        Text.Anchor = prevAnchor;

        string btnText = !string.IsNullOrEmpty(prevVer)
            ? "RimTalk.VersionSwitcher.SwitchToSpecificVer".Translate(prevVer).ToString()
            : "RimTalk.VersionSwitcher.SwitchToPrevious".Translate().ToString();

        if (hasLastDll)
        {
            if (Widgets.ButtonText(btnRect, btnText))
            {
                PromptRollbackConfirmation();
            }
        }
        else
        {
            GUI.color = Color.gray;
            Widgets.ButtonText(btnRect, btnText, active: false);
            GUI.color = Color.white;
            TooltipHandler.TipRegion(btnRect, "RimTalk.VersionSwitcher.NoPreviousDllFound".Translate().ToString());
        }
    }
}
