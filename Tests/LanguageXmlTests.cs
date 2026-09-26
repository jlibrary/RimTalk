using System;
using System.IO;
using System.Linq;
using System.Xml;
using Xunit;

namespace RimTalk.Tests;

public class LanguageXmlTests
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

    [Fact]
    public void AllLanguageXmlFiles_MustBeValidXml()
    {
        string repoRoot = FindRepoRoot();
        string languagesDir = Path.Combine(repoRoot, "Languages");

        Assert.True(Directory.Exists(languagesDir), $"Languages directory not found at {languagesDir}");

        var xmlFiles = Directory.GetFiles(languagesDir, "*.xml", SearchOption.AllDirectories);
        Assert.NotEmpty(xmlFiles);

        var xmlSettings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            IgnoreWhitespace = true
        };

        foreach (var file in xmlFiles)
        {
            string relativePath = Path.GetRelativePath(repoRoot, file);
            try
            {
                using var stream = File.OpenRead(file);
                using var reader = XmlReader.Create(stream, xmlSettings);
                while (reader.Read())
                {
                    // Read through the entire XML file to validate all nodes, attributes, and entities
                }
            }
            catch (XmlException ex)
            {
                Assert.Fail($"XML parse error in '{relativePath}' at line {ex.LineNumber}, position {ex.LinePosition}: {ex.Message}");
            }
        }
    }

    [Fact]
    public void AllLanguages_KeyedXml_MustContainExactSameKeysAsEnglish()
    {
        string repoRoot = FindRepoRoot();
        string languagesDir = Path.Combine(repoRoot, "Languages");

        string englishKeyedDir = Path.Combine(languagesDir, "English", "Keyed");
        Assert.True(Directory.Exists(englishKeyedDir), $"English Keyed directory not found at {englishKeyedDir}");

        var englishKeys = LoadKeyedXmlKeys(englishKeyedDir);
        Assert.NotEmpty(englishKeys);

        var languageDirs = Directory.GetDirectories(languagesDir);
        foreach (var langDir in languageDirs)
        {
            string langName = Path.GetFileName(langDir);
            if (langName.Equals("English", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string keyedDir = Path.Combine(langDir, "Keyed");
            Assert.True(Directory.Exists(keyedDir), $"Keyed directory missing for language: {langName}");

            var langKeys = LoadKeyedXmlKeys(keyedDir);

            var missingFromLang = englishKeys.Except(langKeys).OrderBy(k => k).ToList();
            var extraInLang = langKeys.Except(englishKeys).OrderBy(k => k).ToList();

            Assert.True(
                missingFromLang.Count == 0 && extraInLang.Count == 0,
                $"Language '{langName}' key mismatch! (Total: {langKeys.Count} vs English: {englishKeys.Count})\n" +
                $"Missing ({missingFromLang.Count}): {string.Join(", ", missingFromLang.Take(10))}\n" +
                $"Extra ({extraInLang.Count}): {string.Join(", ", extraInLang.Take(10))}"
            );
        }
    }

    private static System.Collections.Generic.HashSet<string> LoadKeyedXmlKeys(string keyedDir)
    {
        var keys = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        var files = Directory.GetFiles(keyedDir, "*.xml", SearchOption.AllDirectories);

        foreach (var file in files)
        {
            var doc = new XmlDocument();
            doc.Load(file);

            if (doc.DocumentElement == null)
            {
                continue;
            }

            foreach (XmlNode node in doc.DocumentElement.ChildNodes)
            {
                if (node.NodeType == XmlNodeType.Element && !string.IsNullOrWhiteSpace(node.Name))
                {
                    keys.Add(node.Name);
                }
            }
        }

        return keys;
    }
}
