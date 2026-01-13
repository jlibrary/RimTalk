using System;
using System.Collections.Generic;

namespace RimTalk.API;

/// <summary>
/// Strongly-typed context category for hook registration.
/// </summary>
public readonly struct ContextCategory : IEquatable<ContextCategory>
{
    public string Key { get; }
    public ContextType Type { get; }
    
    public ContextCategory(string key, ContextType type = ContextType.Pawn)
    {
        Key = key?.ToLowerInvariant() ?? throw new ArgumentNullException(nameof(key));
        Type = type;
    }
    
    public bool Equals(ContextCategory other) => Key == other.Key && Type == other.Type;
    public override bool Equals(object obj) => obj is ContextCategory c && Equals(c);
    public override int GetHashCode()
    {
        unchecked
        {
            return ((Key?.GetHashCode() ?? 0) * 397) ^ (int)Type;
        }
    }
    public override string ToString() => $"{Type}:{Key}";
    
    public static bool operator ==(ContextCategory left, ContextCategory right) => left.Equals(right);
    public static bool operator !=(ContextCategory left, ContextCategory right) => !left.Equals(right);
    
    public static readonly IEqualityComparer<ContextCategory> Comparer = new CategoryComparer();
    
    private class CategoryComparer : IEqualityComparer<ContextCategory>
    {
        public bool Equals(ContextCategory x, ContextCategory y) => x.Key == y.Key && x.Type == y.Type;
        public int GetHashCode(ContextCategory obj)
        {
            unchecked
            {
                return ((obj.Key?.GetHashCode() ?? 0) * 397) ^ (int)obj.Type;
            }
        }
    }
}

public enum ContextType
{
    Pawn,
    Environment
}