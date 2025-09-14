using System.Collections.Generic;
using RimTalk.Data;
using RimTalk.Util;
using RimWorld;
using Verse;


// Section 1: Data structures for loading old save data.
// We must redefine the old classes here exactly as they were, so the Scribe can load the data from the save.
// These classes are ONLY used for loading and are removed by the migrator.
namespace RimTalk.Data
{
    public class Persona : IExposable
    {
        public string Personality;
        public float TalkInitiationWeight = 1.0f;

        public void ExposeData()
        {
            Scribe_Values.Look(ref Personality, "Personality");
            Scribe_Values.Look(ref TalkInitiationWeight, "TalkInitiationWeight", 1.0f);
        }
    }

    public class PersonaManager : GameComponent
    {
        public Dictionary<int, Persona> Personas = new Dictionary<int, Persona>();

        public PersonaManager(Game game)
        {
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref Personas, "personas", LookMode.Value, LookMode.Deep);
            if (Personas == null)
            {
                Personas = new Dictionary<int, Persona>();
            }
        }
    }
}


// The namespace must match the original exactly for the Scribe to find it during loading.
// This class definition acts as a placeholder to prevent loading errors.
namespace RimTalk
{
    public class PlayLogEntry_RimTalkInteraction : PlayLogEntry_Interaction
    {
        private string _talkContent;

        // The Scribe needs a parameterless constructor to create the object.
        public PlayLogEntry_RimTalkInteraction()
        {
        }

        // We only need the ExposeData method for loading. The other methods are irrelevant.
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref _talkContent, "talkContent");
        }
    }
}


namespace RimTalk.Compatibility
{
    // Section 2: The migration component.
    public class SaveDataMigrator : GameComponent
    {
        private bool _isMigrated = false;

        public SaveDataMigrator(Game game)
        {
        }

        public void MarkAsMigrated()
        {
            _isMigrated = true;
        }

        public static SaveDataMigrator EnsureMigrationComponent()
        {
            var migrator = Current.Game.GetComponent<SaveDataMigrator>();
            if (migrator == null)
            {
                migrator = new SaveDataMigrator(Current.Game);
                Current.Game.components.Add(migrator);
            }

            return migrator;
        }

        public override void LoadedGame()
        {
            base.LoadedGame();

            if (_isMigrated) return;

            Logger.Message("Starting save data migration check.");
            bool migrationPerformed = false;

            // Migrate PersonaManager data to Hediffs
            var oldManager = Current.Game.GetComponent<PersonaManager>();
            if (oldManager != null)
            {
                Logger.Message(
                    $"Found old PersonaManager with {oldManager.Personas.Count} entries. Migrating to hediffs...");
                foreach (var pawn in PawnsFinder.AllMapsAndWorld_Alive)
                {
                    if (oldManager.Personas.TryGetValue(pawn.thingIDNumber, out var persona))
                    {
                        PersonaService.SetPersonality(pawn, persona.Personality);
                        PersonaService.SetTalkInitiationWeight(pawn, persona.TalkInitiationWeight);
                    }
                }

                Current.Game.components.Remove(oldManager);
                Logger.Message("Old PersonaManager removed.");
                migrationPerformed = true;
            }

            // Clean up old PlayLog entries.
            int removedEntries =
                Find.PlayLog.AllEntries.RemoveAll(entry => entry is PlayLogEntry_RimTalkInteraction);
            if (removedEntries > 0)
            {
                Logger.Message($"Removed {removedEntries} legacy log entries.");
                migrationPerformed = true;
            }

            _isMigrated = true;

            if (migrationPerformed)
            {
                Logger.Message("Migration complete. Please re-save your game to finalize the changes.");
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref _isMigrated, "rimTalkIsMigrated", false);
        }
    }
}