using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimTalk.UI
{
    public class MainTabWindow_DebugLauncher : MainTabWindow
    {
        // This remains empty, as it's just a launcher.
        public override void DoWindowContents(Rect inRect)
        {
        }

        public override void PostOpen()
        {
            base.PostOpen();

            var existingWindow = Find.WindowStack.Windows
                .FirstOrDefault(w => w is DebugWindow);

            if (existingWindow != null)
            {
                existingWindow.Close();
            }
            else
            {
                Find.WindowStack.Add(new DebugWindow());
            }

            Close();
        }
    }
}