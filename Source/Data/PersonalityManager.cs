using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RimTalk.Service;
using RimTalk.Util;
using Verse;

namespace RimTalk.Data
{
    public class PersonalityManager : GameComponent
    {
        private Dictionary<int, string> pawnPersonalities = new Dictionary<int, string>();

        public PersonalityManager(Game game) : base() { }

        public string GetPersonality(Pawn pawn)
        {
            return pawnPersonalities.TryGetValue(pawn.thingIDNumber, out var personality)
                ? personality
                : null;
        }

        public void SetPersonality(Pawn pawn, string personality)
        {
            pawnPersonalities[pawn.thingIDNumber] = personality;
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref pawnPersonalities, "pawnPersonalities", LookMode.Value, LookMode.Value);
            if (pawnPersonalities == null)
                pawnPersonalities = new Dictionary<int, string>();
        }

        public async Task<string> GeneratePersona(Pawn pawn)
        {
            string pawnBackstory = PawnService.CreatePawnBackstory(pawn, true);
    
            try
            {
                string response = await AIService.Query($"{Constant.PersonaGenInstruction}\n{pawnBackstory}");
                response = response.Replace("```json", "").Replace("```", "").Trim();
                response = JsonUtil.Sanitize(response);
                
                var result = JsonUtil.DeserializeFromJson<PersonaResponse>(response);
                return result?.persona?.Replace("**", "").Trim();
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