using System;
using Verse;

namespace RimTalk.Util;

public static class Describer
{
    public static string Wealth(float wealthTotal)
    {
        return wealthTotal switch
        {
            < 50_000f => "impecunious",
            < 100_000f => "needy",
            < 200_000f => "just rid of starving",
            < 300_000f => "moderately prosperous",
            < 400_000f => "rich",
            < 600_000f => "luxurious",
            < 1_000_000f => "extravagant",
            < 1_500_000f => "treasures fill the home",
            < 2_000_000f => "as rich as glitter world",
            _ => "richest in the galaxy"
        };
    }
    
    public static string Beauty(float beauty)
    {
        return beauty switch
        {
            > 100f => "wondrously", 
            > 20f => "impressive",
            > 10f => "beautiful",
            > 5f => "decent",
            > -1f => "general",
            > -5f => "awful",
            > -20f => "very awful",
            _ => "disgusting"
        };
    }

    public static string Cleanliness(float cleanliness)
    {
        return cleanliness switch
        {
            > 1.5f => "spotless",
            > 0.5f => "clean",
            > -0.5f => "neat",
            > -1.5f => "a bit dirty",
            > -2.5f => "dirty",
            > -5f => "very dirty",
            _ => "foul"
        };
    }
    
    public static string Resistance(float value)
    {
        if (value <= 0f) return "Completely broken, ready to join";
        if (value < 2f) return "Barely resisting, close to giving in";
        if (value < 6f) return "Weakened, but still cautious";
        if (value < 12f) return "Strong-willed, requires effort";
        return "Extremely defiant, will take a long time";
    }

    public static string Will(float value)
    {
        if (value <= 0f) return "No will left, ready for slavery";
        if (value < 2f) return "Weak-willed, easy to enslave";
        if (value < 6f) return "Moderate will, may resist a little";
        if (value < 12f) return "Strong will, difficult to enslave";
        return "Unyielding, very hard to enslave";
    }

    public static string Suppression(float value)
    {
        if (value < 20f) return "Openly rebellious, likely to resist or escape";
        if (value < 50f) return "Unstable, may push boundaries";
        if (value < 80f) return "Generally obedient, but watchful";
        return "Completely cowed, unlikely to resist";
    }
    
    public static string GetLabelShort(this Gender gender)
    {
        return gender switch
        {
            Gender.Male => "M",
            Gender.Female => "F",
            _ => ""
        };
    }
}