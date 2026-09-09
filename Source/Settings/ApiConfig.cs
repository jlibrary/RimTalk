using System;
using System.Collections.Generic;
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
            return !string.IsNullOrWhiteSpace(CustomModelName) ? CustomModelName : "Local";

        return SelectedModel == "Custom" ? CustomModelName : SelectedModel;
    }

    public static Dictionary<string, object> GetDefaultRequestDict(string model)
    {
        var dict = new Dictionary<string, object>();
        if (string.IsNullOrEmpty(model)) return dict;

        string m = model.ToLower();
        if (m.Contains("gemini"))
        {
            dict["reasoning_effort"] = "low";
        }
        else if (m.Contains("gemma"))
        {
            dict["reasoning_effort"] = "minimal";
        }

        return dict;
    }

    public string GetDefaultRequestJson()
    {
        return GetDefaultRequestJson(GetEffectiveModelName());
    }

    public static string GetDefaultRequestJson(string model)
    {
        var dict = GetDefaultRequestDict(model);
        return dict.Count > 0 ? JsonUtil.SerializeJsonValue(dict, indent: true) : "{}";
    }

    public static string GetDefaultReasoningEffort(string model)
    {
        return GetDefaultRequestDict(model).TryGetValue("reasoning_effort", out var val) ? val?.ToString() : null;
    }

    public bool IsValid()
    {
        if (!IsEnabled) return false;
        if (Provider == AIProvider.Local) return !string.IsNullOrWhiteSpace(BaseUrl);
        bool hasKey = !string.IsNullOrWhiteSpace(ApiKey);
        if (Provider == AIProvider.Player2)
            return (hasKey || Client.Player2.Player2Client.GetLocalAppStatusCached() == true) && SelectedModel != Constant.ChooseModel;
        return hasKey && SelectedModel != Constant.ChooseModel;
    }
}