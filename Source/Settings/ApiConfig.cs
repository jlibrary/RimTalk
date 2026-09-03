using RimTalk.Data;
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
    }

    public string GetEffectiveModelName()
    {
        if (Provider == AIProvider.Local)
            return !string.IsNullOrWhiteSpace(CustomModelName) ? CustomModelName : "Local";

        return SelectedModel == "Custom" ? CustomModelName : SelectedModel;
    }

    public string GetDefaultRequestJson()
    {
        var model = GetEffectiveModelName();
        if (!string.IsNullOrEmpty(model))
        {
            string m = model.ToLower();
            if (m.Contains("gemini") && (m.Contains("pro") || m.Contains("3.7-flash")))
            {
                return "{\n  \"reasoning_effort\": \"low\"\n}";
            }
            if ((m.Contains("gemini") && m.Contains("flash")) || m.Contains("gemma-4"))
            {
                return "{\n  \"reasoning_effort\": \"minimal\"\n}";
            }
        }

        return "{}";
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