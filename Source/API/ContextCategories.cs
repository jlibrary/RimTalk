using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace RimTalk.API;

/// <summary>
/// Predefined context category constants for API stability.
/// </summary>
public static class ContextCategories
{
    public static class Pawn
    {
        public static readonly ContextCategory Name = new("name", ContextType.Pawn);
        public static readonly ContextCategory FullName = new("fullname", ContextType.Pawn);
        public static readonly ContextCategory Gender = new("gender", ContextType.Pawn);
        public static readonly ContextCategory Age = new("age", ContextType.Pawn);
        public static readonly ContextCategory Race = new("race", ContextType.Pawn);
        public static readonly ContextCategory Title = new("title", ContextType.Pawn);
        public static readonly ContextCategory Faction = new("faction", ContextType.Pawn);
        public static readonly ContextCategory Role = new("role", ContextType.Pawn);
        public static readonly ContextCategory Job = new("job", ContextType.Pawn);
        public static readonly ContextCategory Personality = new("personality", ContextType.Pawn);
        public static readonly ContextCategory Mood = new("mood", ContextType.Pawn);
        public static readonly ContextCategory MoodPercent = new("moodpercent", ContextType.Pawn);
        public static readonly ContextCategory Profile = new("profile", ContextType.Pawn);
        public static readonly ContextCategory Backstory = new("backstory", ContextType.Pawn);
        public static readonly ContextCategory Traits = new("traits", ContextType.Pawn);
        public static readonly ContextCategory Skills = new("skills", ContextType.Pawn);
        public static readonly ContextCategory Health = new("health", ContextType.Pawn);
        public static readonly ContextCategory Thoughts = new("thoughts", ContextType.Pawn);
        public static readonly ContextCategory Relations = new("relations", ContextType.Pawn);
        public static readonly ContextCategory Equipment = new("equipment", ContextType.Pawn);
        public static readonly ContextCategory Genes = new("genes", ContextType.Pawn);
        public static readonly ContextCategory Ideology = new("ideology", ContextType.Pawn);
        public static readonly ContextCategory CaptiveStatus = new("captive_status", ContextType.Pawn);
        public static readonly ContextCategory Location = new("location", ContextType.Pawn);
        public static readonly ContextCategory Terrain = new("terrain", ContextType.Pawn);
        public static readonly ContextCategory Beauty = new("beauty", ContextType.Pawn);
        public static readonly ContextCategory Cleanliness = new("cleanliness", ContextType.Pawn);
        public static readonly ContextCategory Surroundings = new("surroundings", ContextType.Pawn);
        
        private static readonly Lazy<IReadOnlyList<ContextCategory>> _all = new(() =>
            typeof(Pawn).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.FieldType == typeof(ContextCategory))
                .Select(f => (ContextCategory)f.GetValue(null))
                .ToList().AsReadOnly());
        
        public static IReadOnlyList<ContextCategory> All => _all.Value;
    }
    
    public static class Environment
    {
        public static readonly ContextCategory Time = new("time", ContextType.Environment);
        public static readonly ContextCategory Date = new("date", ContextType.Environment);
        public static readonly ContextCategory Season = new("season", ContextType.Environment);
        public static readonly ContextCategory Weather = new("weather", ContextType.Environment);
        public static readonly ContextCategory Temperature = new("temperature", ContextType.Environment);
        public static readonly ContextCategory Wealth = new("wealth", ContextType.Environment);
        
        private static readonly Lazy<IReadOnlyList<ContextCategory>> _all = new(() =>
            typeof(Environment).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.FieldType == typeof(ContextCategory))
                .Select(f => (ContextCategory)f.GetValue(null))
                .ToList().AsReadOnly());
        
        public static IReadOnlyList<ContextCategory> All => _all.Value;
    }
    
    public static ContextCategory? TryGetPawnCategory(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        var lowerKey = key.ToLowerInvariant();
        return Pawn.All.FirstOrDefault(c => c.Key == lowerKey);
    }
    
    public static ContextCategory? TryGetEnvironmentCategory(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        var lowerKey = key.ToLowerInvariant();
        return Environment.All.FirstOrDefault(c => c.Key == lowerKey);
    }
}