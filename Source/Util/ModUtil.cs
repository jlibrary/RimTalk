using System.Collections.Generic;
using Verse;

namespace RimTalk.Util;

public static class ModUtil
{
    public static bool IsAnyModActive(string[] packageIds)
    {
        if (packageIds == null) return false;
        try
        {
            for (int i = 0; i < packageIds.Length; i++)
            {
                if (ModsConfig.IsActive(packageIds[i]))
                    return true;
            }
        }
        catch
        {
            // Ignore early initialization or test mock errors
        }

        return false;
    }

    public static string GetActiveModNames(string[] packageIds)
    {
        if (packageIds == null || packageIds.Length == 0) return string.Empty;
        var names = new List<string>(packageIds.Length);
        try
        {
            for (int i = 0; i < packageIds.Length; i++)
            {
                string packageId = packageIds[i];
                if (ModsConfig.IsActive(packageId))
                {
                    var mod = ModLister.GetActiveModWithIdentifier(packageId);
                    if (mod != null && !string.IsNullOrEmpty(mod.Name))
                    {
                        names.Add(mod.Name);
                    }
                }
            }
        }
        catch
        {
            // Ignore early initialization or test mock errors
        }

        return string.Join(", ", names);
    }
}
