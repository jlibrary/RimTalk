using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RimTalk.Util;
using Verse;

namespace RimTalk.Prompt;

/// <summary>
/// Handles import/export of prompt presets in JSON format.
/// Uses JsonUtil for serialization/deserialization.
/// </summary>
public static class PresetSerializer
{
    /// <summary>
    /// Exports a preset to JSON string.
    /// </summary>
    public static string ExportToJson(PromptPreset preset)
    {
        if (preset == null) return null;
        
        try
        {
            var dto = PresetDto.FromPreset(preset);
            return JsonUtil.SerializeToJson(dto);
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to export preset to JSON: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Imports a preset from JSON string.
    /// </summary>
    public static PromptPreset ImportFromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        
        try
        {
            var dto = JsonUtil.DeserializeFromJson<PresetDto>(json);
            if (dto == null) return null;
            
            var preset = dto.ToPreset();
            Logger.Debug($"Successfully imported preset: {preset.Name} with {preset.Entries.Count} entries");
            return preset;
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to import preset: {ex.Message}");
            return null;
        }
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
            
            Logger.Debug($"Exported preset to: {path}");
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