using Verse;

namespace RimTalk.Data
{
    public class ContextSettings : IExposable
    {
        public bool EnableContextOptimization = false;
        public int MaxPawnContextCount = 3;
        public int ConversationHistoryCount = 1;
        
        // Pawn Info
        public bool IncludeRace = true;
        public bool IncludeNotableGenes = true;
        public bool IncludeIdeology = true;
        public bool IncludeBackstory = true;
        public bool IncludeTraits = true;
        public bool IncludeSkills = true;
        public bool IncludeHealth = true;
        public bool IncludeMood = true;
        public bool IncludeThoughts = true;
        public bool IncludeRelations = true;
        public bool IncludeEquipment = true;
        public bool IncludePrisonerSlaveStatus = false;

        // Environment
        public bool IncludeTimeAndDate = true;
        public bool IncludeSeason = true;
        public bool IncludeWeather = true;
        public bool IncludeLocationAndTemperature = false;
        public bool IncludeTerrain = false;
        public bool IncludeBeauty = false;
        public bool IncludeCleanliness = false;
        public bool IncludeSurroundings = false;
        public bool IncludeWealth = false;

        public void ExposeData()
        {
            Scribe_Values.Look(ref EnableContextOptimization, "EnableContextOptimization", false);
            Scribe_Values.Look(ref MaxPawnContextCount, "MaxPawnContextCount", 3);
            Scribe_Values.Look(ref ConversationHistoryCount, "ConversationHistoryCount", 1);
            Scribe_Values.Look(ref IncludeRace, "IncludeRace", true);
            Scribe_Values.Look(ref IncludeNotableGenes, "IncludeNotableGenes", true);
            Scribe_Values.Look(ref IncludeIdeology, "IncludeIdeology", true);
            Scribe_Values.Look(ref IncludeBackstory, "IncludeBackstory", true);
            Scribe_Values.Look(ref IncludeTraits, "IncludeTraits", true);
            Scribe_Values.Look(ref IncludeSkills, "IncludeSkills", true);
            Scribe_Values.Look(ref IncludeHealth, "IncludeHealth", true);
            Scribe_Values.Look(ref IncludeMood, "IncludeMood", true);
            Scribe_Values.Look(ref IncludeThoughts, "IncludeThoughts", true);
            Scribe_Values.Look(ref IncludeRelations, "IncludeRelations", true);
            Scribe_Values.Look(ref IncludeEquipment, "IncludeEquipment", true);
            Scribe_Values.Look(ref IncludePrisonerSlaveStatus, "IncludePrisonerSlaveStatus", false);

            Scribe_Values.Look(ref IncludeTimeAndDate, "IncludeTimeAndDate", true);
            Scribe_Values.Look(ref IncludeSeason, "IncludeSeason", true);
            Scribe_Values.Look(ref IncludeWeather, "IncludeWeather", true);
            Scribe_Values.Look(ref IncludeLocationAndTemperature, "IncludeLocationAndTemperature", false);
            Scribe_Values.Look(ref IncludeTerrain, "IncludeTerrain", false);
            Scribe_Values.Look(ref IncludeBeauty, "IncludeBeauty", false);
            Scribe_Values.Look(ref IncludeCleanliness, "IncludeCleanliness", false);
            Scribe_Values.Look(ref IncludeSurroundings, "IncludeSurroundings", false);
            Scribe_Values.Look(ref IncludeWealth, "IncludeWealth", false);
        }
    }
}
