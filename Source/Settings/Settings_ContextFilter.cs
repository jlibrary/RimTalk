using RimTalk.Data;
using UnityEngine;
using Verse;

namespace RimTalk
{
    public partial class Settings
    {
        private void DrawContextFilterSettings(Listing_Standard listingStandard)
        {
            RimTalkSettings settings = Get();
            var context = settings.Context;
            
            var contextFilterDesc = "RimTalk.Settings.ContextFilterDescription".Translate();
            var contextFilterDescRect = listingStandard.GetRect(Text.CalcHeight(contextFilterDesc, listingStandard.ColumnWidth));
            Widgets.Label(contextFilterDescRect, contextFilterDesc);
            listingStandard.Gap(6f);

            Text.Font = GameFont.Tiny;
            GUI.color = Color.cyan;
            Rect contextFilterTipRect = listingStandard.GetRect(Text.LineHeight);
            Widgets.Label(contextFilterTipRect, "RimTalk.Settings.ContextFilterTip".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            listingStandard.Gap(24f);

            // Define column layout
            const float columnGap = 200f;
            float columnWidth = (listingStandard.ColumnWidth - columnGap) / 2;

            // Get a very small rect just to get the current position
            Rect positionRect = listingStandard.GetRect(0f);
            
            // --- Left Column ---
            Rect leftColumnRect = new Rect(positionRect.x, positionRect.y, columnWidth, 9999f);
            Listing_Standard leftListing = new Listing_Standard();
            leftListing.Begin(leftColumnRect);
            
            Text.Font = GameFont.Small;
            GUI.color = Color.yellow;
            leftListing.Label($"━━ {"RimTalk.Settings.PawnInfo".Translate()} ━━");
            GUI.color = Color.white;
            leftListing.Gap(6f);

            leftListing.CheckboxLabeled("RimTalk.Settings.IncludeRace".Translate(), ref context.IncludeRace);
            leftListing.CheckboxLabeled("RimTalk.Settings.IncludeNotableGenes".Translate(), ref context.IncludeNotableGenes);
            leftListing.CheckboxLabeled("RimTalk.Settings.IncludeIdeology".Translate(), ref context.IncludeIdeology);
            leftListing.CheckboxLabeled("RimTalk.Settings.IncludeBackstory".Translate(), ref context.IncludeBackstory);
            leftListing.CheckboxLabeled("RimTalk.Settings.IncludeTraits".Translate(), ref context.IncludeTraits);
            leftListing.CheckboxLabeled("RimTalk.Settings.IncludeSkills".Translate(), ref context.IncludeSkills);
            leftListing.CheckboxLabeled("RimTalk.Settings.IncludeHealth".Translate(), ref context.IncludeHealth);
            leftListing.CheckboxLabeled("RimTalk.Settings.IncludeMood".Translate(), ref context.IncludeMood);
            leftListing.CheckboxLabeled("RimTalk.Settings.IncludeThoughts".Translate(), ref context.IncludeThoughts);
            leftListing.CheckboxLabeled("RimTalk.Settings.IncludePrisonerSlaveStatus".Translate(), ref context.IncludePrisonerSlaveStatus);
            leftListing.CheckboxLabeled("RimTalk.Settings.IncludeRelations".Translate(), ref context.IncludeRelations);
            leftListing.CheckboxLabeled("RimTalk.Settings.IncludeEquipment".Translate(), ref context.IncludeEquipment);

            leftListing.End();

            // --- Right Column ---
            Rect rightColumnRect = new Rect(leftColumnRect.xMax + columnGap, positionRect.y, columnWidth, 9999f);
            Listing_Standard rightListing = new Listing_Standard();
            rightListing.Begin(rightColumnRect);

            Text.Font = GameFont.Small;
            GUI.color = Color.yellow;
            rightListing.Label($"━━ {"RimTalk.Settings.Environment".Translate()} ━━");
            GUI.color = Color.white;
            rightListing.Gap(6f);

            rightListing.CheckboxLabeled("RimTalk.Settings.IncludeTimeAndDate".Translate(), ref context.IncludeTimeAndDate);
            rightListing.CheckboxLabeled("RimTalk.Settings.IncludeSeason".Translate(), ref context.IncludeSeason);
            rightListing.CheckboxLabeled("RimTalk.Settings.IncludeWeather".Translate(), ref context.IncludeWeather);
            rightListing.CheckboxLabeled("RimTalk.Settings.IncludeLocationAndTemperature".Translate(), ref context.IncludeLocationAndTemperature);
            rightListing.CheckboxLabeled("RimTalk.Settings.IncludeTerrain".Translate(), ref context.IncludeTerrain);
            rightListing.CheckboxLabeled("RimTalk.Settings.IncludeBeauty".Translate(), ref context.IncludeBeauty);
            rightListing.CheckboxLabeled("RimTalk.Settings.IncludeCleanliness".Translate(), ref context.IncludeCleanliness);
            rightListing.CheckboxLabeled("RimTalk.Settings.IncludeSurroundings".Translate(), ref context.IncludeSurroundings);
            rightListing.CheckboxLabeled("RimTalk.Settings.IncludeWealth".Translate(), ref context.IncludeWealth);

            rightListing.End();

            // Advance the main listing standard's vertical position based on the taller of the two columns
            float tallerColumnHeight = Mathf.Max(leftListing.CurHeight, rightListing.CurHeight);
            listingStandard.Gap(tallerColumnHeight);

            listingStandard.Gap(24f);

            // Reset to defaults button
            if (listingStandard.ButtonText("RimTalk.Settings.ResetToDefault".Translate()))
            {
                settings.Context = new ContextSettings();
            }
        }
    }
}