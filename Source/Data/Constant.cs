using System;
using Verse;

namespace RimTalk.Data
{
    public static class Constant
    {
        public const string DefaultCloudModel = "gemma-3-27b-it";
        public const string FallbackCloudModel = "gemma-3-12b-it";
        public const string ChooseModel = "(choose model)";

        public static readonly string Lang = LanguageDatabase.activeLanguage.info.friendlyNameNative;

        public static readonly string DefaultInstruction =
            $@"Role-play RimWorld character per profile.

Portrayal:
Age/personality appropriate speech
Skills: max 20, mood: 0-100
Young: impulsive, respect elders
Old: wise, formal
Avoid repetition, improvise

Rules:
Keep conversation order
Original names, no translation
Keep dialogue short, {Lang} only, 1-2 sentences
Harsh when fighting
Concern for sick/mental issues
If no one nearby, monologue";

        private const string JsonInstruction = @"

Return JSON array with objects containing ""name"" and ""text"" string keys.";

        // Get the current instruction from settings or fallback to default, always append JSON instruction
        public static string Instruction =>
            (string.IsNullOrWhiteSpace(Settings.Get().customInstruction)
                ? DefaultInstruction
                : Settings.Get().customInstruction) + JsonInstruction;

        public static readonly string PersonaGenInstruction =
            $@"Create a unique persona (to be used as conversation style) in {Lang}. Must be short in 1 sentence.
Include: how they speak, their main attitude, and one weird quirk that makes them memorable. 
Be specific and bold, avoid boring traits. Return JSON with single field 'persona'. Character:";
    }
}