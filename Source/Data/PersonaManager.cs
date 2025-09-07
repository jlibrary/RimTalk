using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RimTalk.Service;
using RimTalk.Util;
using Verse;

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
        private Dictionary<int, Persona> _personas = new Dictionary<int, Persona>();
        
        public PersonaManager(Game game) : base() { }

        public string GetPersonality(Pawn pawn)
        {
            return GetPersona(pawn).Personality;
        }

        public void SetPersonality(Pawn pawn, string personality)
        {
            GetPersona(pawn).Personality = personality;
        }

        public float GetTalkInitiationWeight(Pawn pawn)
        {
            if (pawn.IsSlave || pawn.IsPrisoner)
                return 0.3f;
            if (PawnService.IsVisitor(pawn))
                return 0.3f;
            if (PawnService.IsInvader(pawn))
                return 0.3f;
            
            return GetPersona(pawn).TalkInitiationWeight;
        }

        public void SetTalkInitiationWeight(Pawn pawn, float frequency)
        {
            GetPersona(pawn).TalkInitiationWeight = frequency;
        }
        
        private Persona GetPersona(Pawn pawn)
        {
            if (_personas.TryGetValue(pawn.thingIDNumber, out Persona persona))
            {
                return persona;
            }
            
            PersonalityData randomPersonalityData = Constant.Personalities.RandomElement();

            var newPersona = new Persona
            {
                Personality = randomPersonalityData.persona,
                TalkInitiationWeight = randomPersonalityData.chattiness
            };
            _personas[pawn.thingIDNumber] = newPersona;
            return newPersona;
        }

        // public override void ExposeData()
        // {
        //     Scribe_Collections.Look(ref _personas, "personas", LookMode.Value, LookMode.Deep);
        //     if (_personas == null)
        //     {
        //         _personas = new Dictionary<int, Persona>();
        //     }
        // }
        // --- BACKWARD COMPATIBILITY IMPLEMENTATION ---
        public override void ExposeData()
        {
            // Always try to save/load the new format first.
            Scribe_Collections.Look(ref _personas, "personas", LookMode.Value, LookMode.Deep);

            // The migration logic below only runs when loading a save file.
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                // If _personas is null after the above line, it means we are on an old save.
                if (_personas == null)
                {
                    _personas = new Dictionary<int, Persona>(); // Initialize our new dictionary

                    // These are temporary variables to hold the OLD data format.
                    Dictionary<int, string> oldPersonalities = null;

                    // Load the old data structures from the save file.
                    Scribe_Collections.Look(ref oldPersonalities, "pawnPersonalities", LookMode.Value, LookMode.Value);

                    if (oldPersonalities != null)
                    {
                        // This is the migration step. Convert old data into the new structure.
                        foreach (var entry in oldPersonalities)
                        {
                            var newPersona = new Persona { Personality = entry.Value };
                            _personas[entry.Key] = newPersona;
                        }
                    }
                }
            }

            if (_personas == null)
            {
                _personas = new Dictionary<int, Persona>();
            }
        }
        // --- END OF COMPATIBILITY IMPLEMENTATION ---

        public async Task<PersonalityData> GeneratePersona(Pawn pawn)
        {
            string pawnBackstory = PromptService.CreatePawnBackstory(pawn, PromptService.InfoLevel.Full);
    
            try
            {
                string response = await AIService.Query($"{Constant.PersonaGenInstruction}\n{pawnBackstory}");
                response = response.Replace("```json", "").Replace("```", "").Trim();
                response = JsonUtil.Sanitize(response);
                
                // Deserializing directly into PersonalityData.
                var result = JsonUtil.DeserializeFromJson<PersonalityData>(response);

                if (result?.persona != null)
                {
                    result.persona = result.persona.Replace("**", "").Trim();
                }

                return result;
            }
            catch (Exception e)
            {
                Logger.Error(e.Message);
                return null;
            }
        }
        
        public class PersonaResponse 
        { 
            public string persona { get; set; } 
        }
    }
}