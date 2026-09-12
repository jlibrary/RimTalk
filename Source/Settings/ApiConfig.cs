using System.Collections.Generic;
using RimTalk.Client.Player2;
using RimTalk.Data;
using RimTalk.Util;
using Verse;

namespace RimTalk;

public class ApiConfig : IExposable
{
    public bool IsEnabled = true;
    public AIProvider Provider = AIProvider.Google;
    public string ApiKey = "";
    public string SelectedModel = Constant.ChooseModel;
    public string CustomModelName = "";
    public string BaseUrl = "";
    public string CustomRequestJson = "";

    public void ExposeData()
    {
        Scribe_Values.Look(ref IsEnabled, "isEnabled", true);
        Scribe_Values.Look(ref Provider, "provider", AIProvider.Google);
        Scribe_Values.Look(ref ApiKey, "apiKey", "");
        Scribe_Values.Look(ref SelectedModel, "selectedModel", Constant.DefaultCloudModel);
        Scribe_Values.Look(ref CustomModelName, "customModelName", "");
        Scribe_Values.Look(ref BaseUrl, "baseUrl", "");
        Scribe_Values.Look(ref CustomRequestJson, "customRequestJson", "");

        if (Scribe.mode == LoadSaveMode.PostLoadInit && !string.IsNullOrWhiteSpace(CustomRequestJson))
        {
            if (CustomRequestJson.Trim() == GetDefaultRequestJson().Trim())
            {
                CustomRequestJson = "";
            }
        }
    }

    public string GetEffectiveModelName()
    {
        if (Provider == AIProvider.Local)
            return CustomModelName ?? "";

        return SelectedModel == "Custom" ? CustomModelName : SelectedModel;
    }

    public string GetDetectedThinkingLevel()
    {
        var settings = Settings.Get();
        if (settings?.DetectedThinkingLevels != null)
        {
            string modelName = GetEffectiveModelName();
            if (modelName.StartsWith("models/")) modelName = modelName.Substring(7);
            string key = $"{Provider}_{modelName}";
            if (settings.DetectedThinkingLevels.TryGetValue(key, out var level))
                return level;
        }
        return null;
    }

    public Dictionary<string, object> GetDefaultRequestDict()
    {
        var dict = new Dictionary<string, object>();
        string level = GetDetectedThinkingLevel();

        if (level == "disabled")
        {
            dict["thinking"] = new Dictionary<string, object> { ["type"] = "disabled" };
        }
        else if (!string.IsNullOrEmpty(level) && level != "standard")
        {
            dict["reasoning_effort"] = level;
        }

        return dict;
    }

    public string GetDefaultRequestJson()
    {
        var dict = GetDefaultRequestDict();
        return dict.Count > 0 ? JsonUtil.SerializeJsonValue(dict, indent: true) : "{}";
    }

    public bool IsValid()
    {
        if (!IsEnabled) return false;
        if (Provider == AIProvider.Local) return !string.IsNullOrWhiteSpace(BaseUrl);
        bool hasKey = !string.IsNullOrWhiteSpace(ApiKey);
        if (Provider == AIProvider.Player2)
            return (hasKey || Player2Client.GetLocalAppStatusCached() == true) && SelectedModel != Constant.ChooseModel;
        return hasKey && SelectedModel != Constant.ChooseModel;
    }
}