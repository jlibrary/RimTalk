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
                
                if (pawn.IsSlave || pawn.IsPrisoner || PawnService.IsVisitor(pawn) || PawnService.IsInvader(pawn))
                {
                    hediff.TalkInitiationWeight = 0.3f;
                }
                else
                {
                    hediff.TalkInitiationWeight = randomPersonalityData.Chattiness;
                }
                
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
                AIService.UpdateContext($"[Character]\n{pawnBackstory}");
                var request = new TalkRequest(Constant.PersonaGenInstruction, pawn);
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
    }
}