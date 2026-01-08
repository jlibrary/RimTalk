using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using RimTalk.Util;
using Verse;

namespace RimTalk.Prompt;

/// <summary>
/// Handles import/export of prompt presets in JSON format.
/// </summary>
public static class PresetSerializer
{
    // Version for format compatibility
    private const int FORMAT_VERSION = 1;
    
    /// <summary>
    /// Exports a preset to JSON string.
    /// </summary>
    public static string ExportToJson(PromptPreset preset)
    {
        if (preset == null) return null;
        
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine($"  \"version\": {FORMAT_VERSION},");
        sb.AppendLine($"  \"name\": {EscapeJson(preset.Name)},");
        sb.AppendLine($"  \"description\": {EscapeJson(preset.Description)},");
        sb.AppendLine("  \"entries\": [");
        
        for (int i = 0; i < preset.Entries.Count; i++)
        {
            var entry = preset.Entries[i];
            sb.AppendLine("    {");
            sb.AppendLine($"      \"name\": {EscapeJson(entry.Name)},");
            sb.AppendLine($"      \"content\": {EscapeJson(entry.Content)},");
            sb.AppendLine($"      \"role\": \"{entry.Role}\",");
            sb.AppendLine($"      \"position\": \"{entry.Position}\",");
            sb.AppendLine($"      \"inChatDepth\": {entry.InChatDepth},");
            sb.AppendLine($"      \"enabled\": {entry.Enabled.ToString().ToLower()},");
            sb.AppendLine($"      \"editable\": {entry.Editable.ToString().ToLower()}");
            sb.Append("    }");
            if (i < preset.Entries.Count - 1)
                sb.AppendLine(",");
            else
                sb.AppendLine();
        }
        
        sb.AppendLine("  ]");
        sb.Append("}");
        
        return sb.ToString();
    }
    
    /// <summary>
    /// Imports a preset from JSON string.
    /// </summary>
    public static PromptPreset ImportFromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        
        try
        {
            var preset = new PromptPreset();
            
            // Parse version
            var versionMatch = Regex.Match(json, @"""version""\s*:\s*(\d+)");
            int version = versionMatch.Success ? int.Parse(versionMatch.Groups[1].Value) : 1;
            
            // Parse name
            var nameMatch = Regex.Match(json, @"""name""\s*:\s*""((?:[^""\\]|\\.)*)""");
            if (nameMatch.Success)
                preset.Name = UnescapeJson(nameMatch.Groups[1].Value);
            
            // Parse description
            var descMatch = Regex.Match(json, @"""description""\s*:\s*""((?:[^""\\]|\\.)*)""");
            if (descMatch.Success)
                preset.Description = UnescapeJson(descMatch.Groups[1].Value);
            
            // Parse entries array
            var entriesMatch = Regex.Match(json, @"""entries""\s*:\s*\[(.*)\]", RegexOptions.Singleline);
            if (entriesMatch.Success)
            {
                var entriesJson = entriesMatch.Groups[1].Value;
                var entryMatches = Regex.Matches(entriesJson, @"\{([^{}]*(?:\{[^{}]*\}[^{}]*)*)\}", RegexOptions.Singleline);
                
                foreach (Match entryMatch in entryMatches)
                {
                    var entryJson = entryMatch.Groups[1].Value;
                    var entry = ParseEntry(entryJson);
                    if (entry != null)
                    {
                        preset.Entries.Add(entry);
                    }
                }
            }
            
            // Generate new ID for imported preset
            preset.Id = Guid.NewGuid().ToString();
            preset.IsActive = false;
            
            Logger.Message($"Successfully imported preset: {preset.Name} with {preset.Entries.Count} entries");
            return preset;
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to import preset: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Parses a single entry from JSON object content.
    /// </summary>
    private static PromptEntry ParseEntry(string entryJson)
    {
        try
        {
            var entry = new PromptEntry();
            entry.Id = Guid.NewGuid().ToString(); // Generate new ID
            
            // Parse name
            var nameMatch = Regex.Match(entryJson, @"""name""\s*:\s*""((?:[^""\\]|\\.)*)""");
            if (nameMatch.Success)
                entry.Name = UnescapeJson(nameMatch.Groups[1].Value);
            
            // Parse content
            var contentMatch = Regex.Match(entryJson, @"""content""\s*:\s*""((?:[^""\\]|\\.)*)""");
            if (contentMatch.Success)
                entry.Content = UnescapeJson(contentMatch.Groups[1].Value);
            
            // Parse role
            var roleMatch = Regex.Match(entryJson, @"""role""\s*:\s*""(\w+)""");
            if (roleMatch.Success)
            {
                if (Enum.TryParse<PromptRole>(roleMatch.Groups[1].Value, true, out var role))
                    entry.Role = role;
            }
            
            // Parse position
            var posMatch = Regex.Match(entryJson, @"""position""\s*:\s*""(\w+)""");
            if (posMatch.Success)
            {
                if (Enum.TryParse<PromptPosition>(posMatch.Groups[1].Value, true, out var pos))
                    entry.Position = pos;
            }
            
            // Parse inChatDepth
            var depthMatch = Regex.Match(entryJson, @"""inChatDepth""\s*:\s*(\d+)");
            if (depthMatch.Success)
                entry.InChatDepth = int.Parse(depthMatch.Groups[1].Value);
            
            // Parse enabled
            var enabledMatch = Regex.Match(entryJson, @"""enabled""\s*:\s*(true|false)", RegexOptions.IgnoreCase);
            if (enabledMatch.Success)
                entry.Enabled = enabledMatch.Groups[1].Value.ToLower() == "true";
            
            // Parse editable
            var editableMatch = Regex.Match(entryJson, @"""editable""\s*:\s*(true|false)", RegexOptions.IgnoreCase);
            if (editableMatch.Success)
                entry.Editable = editableMatch.Groups[1].Value.ToLower() == "true";
            
            // Imported entries are always editable
            entry.Editable = true;
            entry.SourceModId = null;
            
            return entry;
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to parse entry: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Escapes a string for JSON.
    /// </summary>
    private static string EscapeJson(string s)
    {
        if (s == null) return "\"\"";
        
        var sb = new StringBuilder();
        sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < ' ')
                        sb.Append($"\\u{(int)c:X4}");
                    else
                        sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }
    
    /// <summary>
    /// Unescapes a JSON string.
    /// </summary>
    private static string UnescapeJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        
        var sb = new StringBuilder();
        int i = 0;
        while (i < s.Length)
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                switch (s[i + 1])
                {
                    case '"': sb.Append('"'); i += 2; break;
                    case '\\': sb.Append('\\'); i += 2; break;
                    case 'b': sb.Append('\b'); i += 2; break;
                    case 'f': sb.Append('\f'); i += 2; break;
                    case 'n': sb.Append('\n'); i += 2; break;
                    case 'r': sb.Append('\r'); i += 2; break;
                    case 't': sb.Append('\t'); i += 2; break;
                    case 'u':
                        if (i + 5 < s.Length)
                        {
                            var hex = s.Substring(i + 2, 4);
                            if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int code))
                            {
                                sb.Append((char)code);
                                i += 6;
                            }
                            else
                            {
                                sb.Append(s[i]);
                                i++;
                            }
                        }
                        else
                        {
                            sb.Append(s[i]);
                            i++;
                        }
                        break;
                    default:
                        sb.Append(s[i + 1]);
                        i += 2;
                        break;
                }
            }
            else
            {
                sb.Append(s[i]);
                i++;
            }
        }
        return sb.ToString();
    }
    
    /// <summary>
    /// Gets the default export directory path.
    /// </summary>
    public static string GetExportDirectory()
    {
        var path = Path.Combine(GenFilePaths.ConfigFolderPath, "RimTalk", "Presets");
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
        return path;
    }
    
    /// <summary>
    /// Exports a preset to a file.
    /// </summary>
    public static bool ExportToFile(PromptPreset preset, string filename = null)
    {
        try
        {
            var json = ExportToJson(preset);
            if (json == null) return false;
            
            if (string.IsNullOrEmpty(filename))
            {
                // Sanitize preset name for filename
                filename = SanitizeFilename(preset.Name);
            }
            
            var path = Path.Combine(GetExportDirectory(), filename + ".json");
            File.WriteAllText(path, json, Encoding.UTF8);
            
            Logger.Message($"Exported preset to: {path}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to export preset: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Imports a preset from a file.
    /// </summary>
    public static PromptPreset ImportFromFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                Logger.Warning($"Preset file not found: {path}");
                return null;
            }
            
            var json = File.ReadAllText(path, Encoding.UTF8);
            return ImportFromJson(json);
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to import preset: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Gets all available preset files.
    /// </summary>
    public static List<string> GetAvailablePresetFiles()
    {
        var dir = GetExportDirectory();
        if (!Directory.Exists(dir)) return new List<string>();
        
        return Directory.GetFiles(dir, "*.json")
            .OrderBy(f => Path.GetFileName(f))
            .ToList();
    }
    
    /// <summary>
    /// Sanitizes a string for use as a filename.
    /// </summary>
    private static string SanitizeFilename(string name)
    {
        if (string.IsNullOrEmpty(name)) return "preset";
        
        var invalidChars = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder();
        foreach (char c in name)
        {
            if (!invalidChars.Contains(c))
                sb.Append(c);
            else
                sb.Append('_');
        }
        return sb.ToString();
    }
}