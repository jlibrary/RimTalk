using System;
using System.Threading.Tasks;
using RimTalk.Service;
using RimTalk.Util;
using Verse;

namespace RimTalk.Data
{
    public static class PersonaService
    {
        private static Hediff_Persona GetOrAddPersonaHediff(Pawn pawn)
        {
            var hediff = pawn.health.hediffSet.GetFirstHediffOfDef(HediffDef.Named(Hediff_Persona.RimtalkHediff)) as Hediff_Persona;
            if (hediff == null)
            {
                hediff = (Hediff_Persona)HediffMaker.MakeHediff(HediffDef.Named(Hediff_Persona.RimtalkHediff), pawn);
                
                // Assign a random personality on creation, similar to old logic
                PersonalityData randomPersonalityData = Constant.Personalities.RandomElement();
                hediff.Personality = randomPersonalityData.Persona;
                hediff.TalkInitiationWeight = randomPersonalityData.Chattiness;
                
                pawn.health.AddHediff(hediff);
            }
            return hediff;
        }

        public static string GetPersonality(Pawn pawn)
        {
            return GetOrAddPersonaHediff(pawn).Personality;
        }

        public static void SetPersonality(Pawn pawn, string personality)
        {
            GetOrAddPersonaHediff(pawn).Personality = personality;
        }

        public static float GetTalkInitiationWeight(Pawn pawn)
        {
            if (pawn.IsSlave || pawn.IsPrisoner)
                return 0.3f;
            if (PawnService.IsVisitor(pawn))
                return 0.3f;
            if (PawnService.IsInvader(pawn))
                return 0.3f;

            return GetOrAddPersonaHediff(pawn).TalkInitiationWeight;
        }

        public static void SetTalkInitiationWeight(Pawn pawn, float frequency)
        {
            GetOrAddPersonaHediff(pawn).TalkInitiationWeight = frequency;
        }

        public static async Task<PersonalityData> GeneratePersona(Pawn pawn)
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