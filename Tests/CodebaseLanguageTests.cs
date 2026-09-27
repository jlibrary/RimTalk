using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace RimTalk.Tests;

public class CodebaseLanguageTests
{
    private static readonly Regex LanguageKeywordRegex = new(
        @"\b(chinese|chinesesimplified|chinesetraditional|french|japanese|korean|russian)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "RimTalk.csproj")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Could not find RimTalk repository root.");
    }

    [Fact]
    public void Codebase_MustNotContain_NonEnglishCharactersOrLanguageKeywords()
    {
        string repoRoot = FindRepoRoot();
        var targetDirs = new[] { "Source", "Tests" };

        var csFiles = targetDirs
            .Select(d => Path.Combine(repoRoot, d))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.GetFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) &&
                        !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
            .Where(f => !Path.GetFileName(f).Equals(nameof(CodebaseLanguageTests) + ".cs", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f)
            .ToList();

        Assert.NotEmpty(csFiles);

        var violations = new List<string>();

        foreach (var file in csFiles)
        {
            string relativePath = Path.GetRelativePath(repoRoot, file);
            var lines = File.ReadAllLines(file);

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];

                // 1. Non-English letter check: any letter character beyond ASCII (> 127)
                var nonEnglishLetters = line.Where(c => char.IsLetter(c) && c > 127).Distinct().ToArray();
                if (nonEnglishLetters.Length > 0)
                {
                    violations.Add($"[{relativePath}:{i + 1}] Non-English character(s) '{new string(nonEnglishLetters)}': {line.Trim()}");
                }

                // 2. Language keyword check
                if (LanguageKeywordRegex.IsMatch(line))
                {
                    violations.Add($"[{relativePath}:{i + 1}] Language keyword: {line.Trim()}");
                }
            }
        }

        Assert.True(violations.Count == 0,
            $"Found {violations.Count} non-English or language keyword violation(s):\n" + string.Join("\n", violations));
    }
}
