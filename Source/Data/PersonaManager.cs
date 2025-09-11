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
                Personality = randomPersonalityData.Persona,
                TalkInitiationWeight = randomPersonalityData.Chattiness
            };
            _personas[pawn.thingIDNumber] = newPersona;
            return newPersona;
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref _personas, "personas", LookMode.Value, LookMode.Deep);
            if (_personas == null)
            {
                _personas = new Dictionary<int, Persona>();
            }
        }

        public async Task<PersonalityData> GeneratePersona(Pawn pawn)
        {
            string pawnBackstory = PromptService.CreatePawnBackstory(pawn, PromptService.InfoLevel.Full);
    
            try
            {
                string prompt = $"{Constant.PersonaGenInstruction}\n{pawnBackstory}";
                var request = new TalkRequest(prompt, pawn);
                PersonalityData personalityData = await AIService.Query<PersonalityData>(request);
                

                if (personalityData?.Persona != null)
                {
                    personalityData.Persona = personalityData.Persona.Replace("**", "").Trim();
                }

                return personalityData;
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