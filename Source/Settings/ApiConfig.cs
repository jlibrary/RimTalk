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

    public void ExposeData()
    {
        Scribe_Values.Look(ref IsEnabled, "isEnabled", true);
        Scribe_Values.Look(ref Provider, "provider", AIProvider.Google);
        Scribe_Values.Look(ref ApiKey, "apiKey", "");
        Scribe_Values.Look(ref SelectedModel, "selectedModel", Constant.DefaultCloudModel);
        Scribe_Values.Look(ref CustomModelName, "customModelName", "");
        Scribe_Values.Look(ref BaseUrl, "baseUrl", "");
    }

    public bool IsValid()
    {
        if (!IsEnabled) return false;
            
        if (Settings.Get().UseCloudProviders)
        {
            // Player2 can work without API key (local app detection)
            if (Provider == AIProvider.Player2)
                return SelectedModel != Constant.ChooseModel;
                
            return !string.IsNullOrWhiteSpace(ApiKey) && SelectedModel != Constant.ChooseModel;
        }
        else
            return !string.IsNullOrWhiteSpace(BaseUrl);
    }
}