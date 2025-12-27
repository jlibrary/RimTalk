using System.Linq;
using RimTalk.Util;
using RimWorld;
using Verse;

namespace RimTalk
{
    public class MemoryCleanUp : GameComponent
    {
        public MemoryCleanUp(Game game)
        {
        }

        public override void FinalizeInit()
        {
            FixIncompatibleMemories();
        }

        private void FixIncompatibleMemories()
        {
            string[] cumulativeDefs =
            [
                "Chitchat", 
                "RimTalk_Chitchat"
            ];

            int fixedCount = 0;

            if (Current.Game == null || Current.Game.Maps == null) return;

            foreach (Map map in Current.Game.Maps)
            {
                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                {
                    var memHandler = pawn.needs?.mood?.thoughts?.memories;
                    if (memHandler == null) continue;

                    for (int i = memHandler.Memories.Count - 1; i >= 0; i--)
                    {
                        var memory = memHandler.Memories[i];
                        
                        if (cumulativeDefs.Contains(memory.def.defName))
                        {
                            if (!(memory is Thought_MemorySocialCumulative))
                            {
                                memHandler.RemoveMemory(memory);
                                fixedCount++;
                            }
                        }
                    }
                }
            }

            if (fixedCount > 0)
            {
                Logger.Message($"Fixed {fixedCount} incompatible memories to prevent crashes.");
            }
        }

        public override void ExposeData() { }
    }
}