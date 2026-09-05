using System;
using System.Linq;
using RimTalk.API;
using Verse;
using Xunit;

namespace RimTalk.Tests;

public class ContextHookRegistryTests : IDisposable
{
    public ContextHookRegistryTests()
    {
        ContextHookRegistry.Clear();
    }

    public void Dispose()
    {
        ContextHookRegistry.Clear();
    }

    [Fact]
    public void ApplyPawnHooks_FaultyAddonThrows_CatchesAndReturnsOriginalValue()
    {
        var pawn = new Pawn { Name = "TestColonist" };
        var category = ContextCategories.Pawn.Backstory;

        // Register a buggy addon hook that throws NullReferenceException
        ContextHookRegistry.RegisterPawnHook(category, ContextHookRegistry.HookOperation.Append, "BuggyMod",
            (p, original) => throw new NullReferenceException("Simulated addon crash in hook!"), priority: 50);

        // Applying hooks should NOT throw; it must catch the exception and preserve the original value
        string result = ContextHookRegistry.ApplyPawnHooks(category, pawn, "OriginalBackstory");

        Assert.Equal("OriginalBackstory", result);
    }

    [Fact]
    public void ApplyPawnHooks_PriorityOrdering_ExecutesInOrder()
    {
        var pawn = new Pawn { Name = "TestColonist" };
        var category = ContextCategories.Pawn.Traits;

        // Register low priority (runs later)
        ContextHookRegistry.RegisterPawnHook(category, ContextHookRegistry.HookOperation.Append, "ModLate",
            (p, text) => text + " [Late]", priority: 200);

        // Register high priority (runs earlier)
        ContextHookRegistry.RegisterPawnHook(category, ContextHookRegistry.HookOperation.Append, "ModEarly",
            (p, text) => text + " [Early]", priority: 50);

        string result = ContextHookRegistry.ApplyPawnHooks(category, pawn, "Traits:");

        Assert.Equal("Traits: [Early] [Late]", result);
    }

    [Fact]
    public void ApplyPawnHooks_OverrideOperation_OverridesValue()
    {
        var pawn = new Pawn { Name = "TestColonist" };
        var category = ContextCategories.Pawn.Personality;

        ContextHookRegistry.RegisterPawnHook(category, ContextHookRegistry.HookOperation.Override, "OverrideMod",
            (p, text) => "CustomPersonalityOverride", priority: 100);

        string result = ContextHookRegistry.ApplyPawnHooks(category, pawn, "DefaultPersonality");

        Assert.Equal("CustomPersonalityOverride", result);
    }

    [Fact]
    public void InjectedSections_AnchorAndPosition_OrderedCorrectly()
    {
        var anchor = ContextCategories.Pawn.Health;

        ContextHookRegistry.InjectPawnSection("PreHealth", "ModA", anchor, ContextHookRegistry.InjectPosition.Before,
            p => "PreHealthContent", priority: 10);
        ContextHookRegistry.InjectPawnSection("PostHealth", "ModB", anchor, ContextHookRegistry.InjectPosition.After,
            p => "PostHealthContent", priority: 10);

        var sections = ContextHookRegistry.GetInjectedSectionsAt(anchor).ToList();

        Assert.Equal(2, sections.Count);
        Assert.Equal(ContextHookRegistry.InjectPosition.Before, sections[0].Position);
        Assert.Equal("PreHealth", sections[0].Name);

        Assert.Equal(ContextHookRegistry.InjectPosition.After, sections[1].Position);
        Assert.Equal("PostHealth", sections[1].Name);
    }

    [Fact]
    public void UnregisterMod_RemovesOnlyTargetModHooks()
    {
        var pawn = new Pawn { Name = "TestColonist" };
        var category = ContextCategories.Pawn.Job;

        ContextHookRegistry.RegisterPawnHook(category, ContextHookRegistry.HookOperation.Append, "ModToKeep",
            (p, text) => text + " Keep", priority: 100);
        ContextHookRegistry.RegisterPawnHook(category, ContextHookRegistry.HookOperation.Append, "ModToRemove",
            (p, text) => text + " Remove", priority: 100);

        // Unregister ModToRemove
        ContextHookRegistry.UnregisterMod("ModToRemove");

        string result = ContextHookRegistry.ApplyPawnHooks(category, pawn, "Job:");

        Assert.Equal("Job: Keep", result);
        Assert.DoesNotContain("Remove", result);
    }
}
