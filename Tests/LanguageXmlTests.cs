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
}
