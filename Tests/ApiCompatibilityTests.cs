#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Xunit;

namespace RimTalk.Tests;

public class ApiCompatibilityTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "RimTalk.csproj")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Could not find RimTalk repository root.");
    }

    private static string? GetCurrentAssemblyPath(string repoRoot)
    {
        var candidates = new[]
        {
            Path.Combine(repoRoot, "1.6", "Assemblies", "RimTalk.dll"),
            Path.Combine(repoRoot, "Release_Mod", "1.6", "Assemblies", "RimTalk.dll"),
            Path.Combine(repoRoot, "obj", "Release", "RimTalk.dll"),
            "/Users/chris/Library/Application Support/Steam/steamapps/common/RimWorld/RimWorldMac.app/Mods/RimTalk/1.6/Assemblies/RimTalk.dll"
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    [Fact]
    public void VerifyPublicApiBackwardCompatibility_WithLastVersion()
    {
        string repoRoot = FindRepoRoot();
        string baselinePath = Path.Combine(repoRoot, "LastVersion", "1.6", "RimTalk.dll");
        Assert.True(File.Exists(baselinePath), $"Baseline assembly not found at: {baselinePath}");

        string? currentPath = GetCurrentAssemblyPath(repoRoot);
        Assert.True(currentPath != null && File.Exists(currentPath),
            "Current 1.6 RimTalk.dll not found. Please build the project (or run ./build.sh) before running ABI tests.");

        using var baselineAssembly = AssemblyDefinition.ReadAssembly(baselinePath);
        using var currentAssembly = AssemblyDefinition.ReadAssembly(currentPath);

        var baselineTypes = baselineAssembly.MainModule.Types
            .Where(t => t.IsPublic || t.IsNestedPublic)
            .ToDictionary(t => t.FullName);

        var currentTypes = currentAssembly.MainModule.Types
            .Where(t => t.IsPublic || t.IsNestedPublic)
            .ToDictionary(t => t.FullName);

        var breakingChanges = new List<string>();

        foreach (var (typeName, baseType) in baselineTypes)
        {
            // UI classes are internal windows and widgets, not part of third-party addon public contracts
            if (typeName.StartsWith("RimTalk.UI."))
                continue;

            if (!currentTypes.TryGetValue(typeName, out var currType))
            {
                breakingChanges.Add($"Missing Type: Public type '{typeName}' was removed or made non-public.");
                continue;
            }

            // Check enum values
            if (baseType.IsEnum)
            {
                var baseEnumFields = baseType.Fields.Where(f => f.IsStatic && f.IsLiteral).ToList();
                var currEnumFields = currType.Fields.Where(f => f.IsStatic && f.IsLiteral).ToDictionary(f => f.Name);

                // TalkType shifted in v1.2.0 when Interaction was added, which has already shipped.
                // We pin its exact v1.2.x layout here to ensure it NEVER changes again in future versions.
                if (typeName == "RimTalk.Source.Data.TalkType")
                {
                    var expectedTalkTypes = new Dictionary<string, int>
                    {
                        ["Urgent"] = 0,
                        ["Hediff"] = 1,
                        ["LevelUp"] = 2,
                        ["Chitchat"] = 3,
                        ["Interaction"] = 4,
                        ["Event"] = 5,
                        ["QuestOffer"] = 6,
                        ["QuestEnd"] = 7,
                        ["Thought"] = 8,
                        ["User"] = 9,
                        ["Announcement"] = 10,
                        ["Sleep"] = 11,
                        ["Other"] = 12
                    };

                    foreach (var (name, expectedVal) in expectedTalkTypes)
                    {
                        if (!currEnumFields.TryGetValue(name, out var currField))
                        {
                            breakingChanges.Add($"Missing Enum Member: '{typeName}.{name}' was removed.");
                        }
                        else if ((int)currField.Constant != expectedVal)
                        {
                            breakingChanges.Add($"Enum Value Changed: '{typeName}.{name}' is currently {(int)currField.Constant}, but must remain {expectedVal} to preserve v1.2.x compatibility.");
                        }
                    }
                    continue;
                }

                foreach (var baseField in baseEnumFields)
                {
                    if (!currEnumFields.TryGetValue(baseField.Name, out var currField))
                    {
                        breakingChanges.Add($"Missing Enum Member: '{typeName}.{baseField.Name}' was removed.");
                    }
                    else if (!Equals(baseField.Constant, currField.Constant))
                    {
                        breakingChanges.Add($"Enum Value Changed: '{typeName}.{baseField.Name}' changed value from {baseField.Constant} to {currField.Constant}.");
                    }
                }
                continue;
            }

            // Check public methods
            var baseMethods = baseType.Methods.Where(m => m.IsPublic && !m.IsSpecialName);
            foreach (var baseMethod in baseMethods)
            {
                string methodSig = GetMethodSignature(baseMethod);
                bool matchFound = currType.Methods.Any(currMethod =>
                    currMethod.IsPublic &&
                    !currMethod.IsSpecialName &&
                    currMethod.Name == baseMethod.Name &&
                    SignaturesMatch(baseMethod, currMethod));

                if (!matchFound)
                {
                    breakingChanges.Add($"Missing/Modified Method: '{typeName}.{methodSig}' was removed or its parameter/return types changed.");
                }
            }

            // Check public constructors
            var baseConstructors = baseType.Methods.Where(m => m.IsPublic && m.IsConstructor);
            foreach (var baseCtor in baseConstructors)
            {
                string ctorSig = GetMethodSignature(baseCtor);
                bool matchFound = currType.Methods.Any(currCtor =>
                    currCtor.IsPublic &&
                    currCtor.IsConstructor &&
                    SignaturesMatch(baseCtor, currCtor));

                if (!matchFound)
                {
                    breakingChanges.Add($"Missing/Modified Constructor: '{typeName}.{ctorSig}' was removed or modified.");
                }
            }

            // Check public fields
            var baseFields = baseType.Fields.Where(f => f.IsPublic);
            foreach (var baseField in baseFields)
            {
                var currField = currType.Fields.FirstOrDefault(f => f.IsPublic && f.Name == baseField.Name);
                if (currField == null)
                {
                    breakingChanges.Add($"Missing Field: Public field '{typeName}.{baseField.Name}' was removed.");
                }
                else if (baseField.FieldType.FullName != currField.FieldType.FullName)
                {
                    breakingChanges.Add($"Field Type Changed: '{typeName}.{baseField.Name}' changed type from {baseField.FieldType.FullName} to {currField.FieldType.FullName}.");
                }
            }
        }

        Assert.True(breakingChanges.Count == 0,
            $"Found {breakingChanges.Count} ABI breaking changes compared to LastVersion:\n" +
            string.Join("\n", breakingChanges));
    }

    private static bool SignaturesMatch(MethodDefinition a, MethodDefinition b)
    {
        if (a.ReturnType.FullName != b.ReturnType.FullName) return false;
        if (a.Parameters.Count != b.Parameters.Count) return false;

        for (int i = 0; i < a.Parameters.Count; i++)
        {
            if (a.Parameters[i].ParameterType.FullName != b.Parameters[i].ParameterType.FullName)
                return false;
        }

        return true;
    }

    private static string GetMethodSignature(MethodDefinition m)
    {
        var paramTypes = string.Join(", ", m.Parameters.Select(p => p.ParameterType.Name));
        return $"{m.ReturnType.Name} {m.Name}({paramTypes})";
    }
}
