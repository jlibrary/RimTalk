using System;
using System.Collections.Generic;
using Scriban;
using Scriban.Runtime;
using Xunit;

namespace RimTalk.Tests;

public class PromptTemplateTests
{
    [Fact]
    public void Scenario_PromptPreset_InChatDepth_InsertsPromptAtExactHistoryOffset()
    {
        // Tests the core SillyTavern-style InChat anchoring logic in PromptPreset:
        // Ensures entries with InChatDepth insert relative to chat history end
        var preset = new RimTalk.Prompt.PromptPreset("TestPreset");
        
        var sysEntry = new RimTalk.Prompt.PromptEntry("System", "System prompt", RimTalk.Prompt.PromptRole.System)
        {
            Position = RimTalk.Prompt.PromptPosition.Relative
        };
        var histEntry = new RimTalk.Prompt.PromptEntry("History", "{{chat.history}}", RimTalk.Prompt.PromptRole.User)
        {
            Position = RimTalk.Prompt.PromptPosition.Relative,
            IsMainChatHistory = true
        };
        var inChatReminder = new RimTalk.Prompt.PromptEntry("Reminder", "Remember: Stay in character!", RimTalk.Prompt.PromptRole.System, inChatDepth: 1)
        {
            Position = RimTalk.Prompt.PromptPosition.InChat
        };
        var promptEntry = new RimTalk.Prompt.PromptEntry("Prompt", "User prompt here", RimTalk.Prompt.PromptRole.User)
        {
            Position = RimTalk.Prompt.PromptPosition.Relative
        };

        preset.AddEntry(sysEntry);
        preset.AddEntry(histEntry);
        preset.AddEntry(inChatReminder);
        preset.AddEntry(promptEntry);

        var relEntries = preset.GetRelativeEntries().ToList();
        var inChatEntries = preset.GetInChatEntries().ToList();

        Assert.Equal(3, relEntries.Count);
        Assert.Single(inChatEntries);
        Assert.Equal("Reminder", inChatEntries[0].Name);
        Assert.Equal(1, inChatEntries[0].InChatDepth);
    }

    [Fact]
    public void Scenario_RoleMerging_ConsecutiveRolesCombinedExceptAcrossMergeBoundary()
    {
        // Tests the merge consecutive roles pipeline:
        // Gemini and modern LLMs crash if given adjacent User-User or System-System messages.
        // But chat history must NOT accidentally merge into the current user prompt.
        var messages = new List<(RimTalk.Prompt.PromptRole role, string content)>
        {
            (RimTalk.Prompt.PromptRole.System, "Base instruction."),
            (RimTalk.Prompt.PromptRole.System, "JSON format."),
            (RimTalk.Prompt.PromptRole.User, "History turn 1."),
            (RimTalk.Prompt.PromptRole.Assistant, "History turn 2."),
            (RimTalk.Prompt.PromptRole.User, "Current prompt.") // mergeBoundary at index 4 prevents merging with History turn 1 if adjacent
        };

        // Test the production PromptMessageRoleMerger directly:
        var merged = RimTalk.Prompt.PromptMessageRoleMerger.MergeConsecutiveRoles(messages, mergeBoundary: 4);

        // The two System messages must be merged into one single System message:
        Assert.Equal(4, merged.Count);
        Assert.Equal(RimTalk.Prompt.PromptRole.System, merged[0].role);
        Assert.Contains("Base instruction.\n\nJSON format.", merged[0].content);
        Assert.Equal(RimTalk.Prompt.PromptRole.User, merged[1].role);
        Assert.Equal("History turn 1.", merged[1].content);
        Assert.Equal(RimTalk.Prompt.PromptRole.Assistant, merged[2].role);
        Assert.Equal("History turn 2.", merged[2].content);
        Assert.Equal(RimTalk.Prompt.PromptRole.User, merged[3].role);
        Assert.Equal("Current prompt.", merged[3].content);
    }

    [Fact]
    public void Scenario_CustomRolePrefix_AppliesRoleTagCorrectly()
    {
        // When a preset entry specifies a CustomRole (e.g. "Narrator" or "GM"),
        // it must map to User role with [role: CustomRole] prefix
        var entry = new RimTalk.Prompt.PromptEntry("CustomNarrator", "Describe the rimworld sunrise.")
        {
            Role = RimTalk.Prompt.PromptRole.System,
            CustomRole = "Narrator"
        };

        var effectiveRole = RimTalk.Prompt.PromptPresetAssembler.GetEffectiveRole(entry);
        var finalContent = RimTalk.Prompt.PromptPresetAssembler.ApplyCustomRolePrefix(entry, entry.Content);

        Assert.Equal(RimTalk.Prompt.PromptRole.User, effectiveRole);
        Assert.StartsWith("[role: Narrator]\n", finalContent);
        Assert.Contains("Describe the rimworld sunrise.", finalContent);
    }

    [Fact]
    public void ComplexPreset_AssemblesMultipleInChatDepths_CustomRoles_AndProtectsHistoryBoundary()
    {
        // Tests full complex preset behavior matching SillyTavern character cards:
        // 1. Base System Instruction
        // 2. Main Chat History marker (3 turns: User, Assistant, User)
        // 3. InChat System reminder at depth 1 (should be injected before the latest history turn)
        // 4. InChat Custom Role entry ("Narrator") at depth 0 (should be injected right after history end)
        // 5. Current dialogue prompt with Custom Role ("GM")
        var preset = new RimTalk.Prompt.PromptPreset("ComplexRPPreset");

        preset.AddEntry(new RimTalk.Prompt.PromptEntry("System", "You are roleplaying in a harsh RimWorld colony.", RimTalk.Prompt.PromptRole.System)
        {
            Position = RimTalk.Prompt.PromptPosition.Relative
        });

        preset.AddEntry(new RimTalk.Prompt.PromptEntry("HistoryMarker", "{{chat.history}}", RimTalk.Prompt.PromptRole.User)
        {
            Position = RimTalk.Prompt.PromptPosition.Relative,
            IsMainChatHistory = true
        });

        // InChat reminder at depth 1:
        preset.AddEntry(new RimTalk.Prompt.PromptEntry("ReminderDepth1", "Stay faithful to medieval tech restrictions.", RimTalk.Prompt.PromptRole.System, inChatDepth: 1)
        {
            Position = RimTalk.Prompt.PromptPosition.InChat
        });

        // InChat custom role at depth 0:
        preset.AddEntry(new RimTalk.Prompt.PromptEntry("NarratorDepth0", "The sky turns dark with volcanic ash.", RimTalk.Prompt.PromptRole.System, inChatDepth: 0)
        {
            Position = RimTalk.Prompt.PromptPosition.InChat,
            CustomRole = "Narrator"
        });

        // User Prompt at end:
        preset.AddEntry(new RimTalk.Prompt.PromptEntry("FinalPrompt", "Colonist Val picks up a bow.", RimTalk.Prompt.PromptRole.User)
        {
            Position = RimTalk.Prompt.PromptPosition.Relative
        });

        var fakeHistory = new List<(RimTalk.Data.Role role, string message)>
        {
            (RimTalk.Data.Role.User, "Turn 1: Where are we?"),
            (RimTalk.Data.Role.AI, "Turn 2: Near the ruins."),
            (RimTalk.Data.Role.User, "Turn 3: I hear mechanoids.")
        };

        var segments = new List<RimTalk.Data.PromptMessageSegment>();
        var assembled = RimTalk.Prompt.PromptPresetAssembler.AssembleMessages(
            preset,
            content => content,
            fakeHistory,
            segments);

        // 1. Base instruction is first
        Assert.Equal(RimTalk.Prompt.PromptRole.System, assembled[0].role);
        Assert.Contains("harsh RimWorld colony", assembled[0].content);

        // 2. Chat history exists
        Assert.Contains(assembled, m => m.content.Contains("Turn 1: Where are we?"));
        Assert.Contains(assembled, m => m.content.Contains("Turn 2: Near the ruins."));

        // 3. InChat reminder at depth 1 is present in the sequence
        Assert.Contains(assembled, m => m.content.Contains("medieval tech restrictions"));

        // 4. InChat custom role entry is present and has "[role: Narrator]" prefix with User role
        var narratorMsg = assembled.FirstOrDefault(m => m.content.Contains("volcanic ash"));
        Assert.NotNull(narratorMsg.content);
        Assert.Equal(RimTalk.Prompt.PromptRole.User, narratorMsg.role);
        Assert.StartsWith("[role: Narrator]\n", narratorMsg.content);

        // 5. Final user prompt is present
        Assert.Contains(assembled, m => m.content.Contains("Colonist Val picks up a bow."));
        Assert.True(segments.Count >= 5);
    }

    [Fact]
    public void AssembleMessages_HistoryRaw_EmitsAlternatingMessages_WhileDefaultHistoryEmitsSingleBlock()
    {
        var fakeHistory = new List<(RimTalk.Data.Role role, string message)>
        {
            (RimTalk.Data.Role.User, "Turn 1: Where are we?"),
            (RimTalk.Data.Role.AI, "Turn 2: Near the ruins."),
            (RimTalk.Data.Role.User, "Turn 3: I hear mechanoids.")
        };

        // 1. Default preset using {{chat.history}} -> Single User message block
        var defaultPreset = new RimTalk.Prompt.PromptPreset("DefaultPreset");
        defaultPreset.AddEntry(new RimTalk.Prompt.PromptEntry("HistoryMarker", "{{chat.history}}", RimTalk.Prompt.PromptRole.User)
        {
            Position = RimTalk.Prompt.PromptPosition.Relative,
            IsMainChatHistory = true
        });

        var defaultAssembled = RimTalk.Prompt.PromptPresetAssembler.AssembleMessages(
            defaultPreset,
            content => content,
            fakeHistory);

        Assert.Single(defaultAssembled);
        Assert.Equal(RimTalk.Prompt.PromptRole.User, defaultAssembled[0].role);
        Assert.StartsWith(RimTalk.Prompt.PromptPresetAssembler.ChatHistoryHeader, defaultAssembled[0].content);
        Assert.Contains("Turn 1: Where are we?", defaultAssembled[0].content);
        Assert.Contains("Turn 2: Near the ruins.", defaultAssembled[0].content);

        // 2. Preset using {{chat.history_raw}} -> Revives alternating multi-message objects
        var rawPreset = new RimTalk.Prompt.PromptPreset("RawPreset");
        rawPreset.AddEntry(new RimTalk.Prompt.PromptEntry("HistoryMarker", "{{chat.history_raw}}", RimTalk.Prompt.PromptRole.User)
        {
            Position = RimTalk.Prompt.PromptPosition.Relative,
            IsMainChatHistory = true
        });

        var rawAssembled = RimTalk.Prompt.PromptPresetAssembler.AssembleMessages(
            rawPreset,
            content => content,
            fakeHistory);

        Assert.Equal(3, rawAssembled.Count);
        Assert.Equal(RimTalk.Prompt.PromptRole.User, rawAssembled[0].role);
        Assert.Equal("Turn 1: Where are we?", rawAssembled[0].role == RimTalk.Prompt.PromptRole.User ? rawAssembled[0].content : "");
        Assert.Equal(RimTalk.Prompt.PromptRole.Assistant, rawAssembled[1].role);
        Assert.Equal("Turn 2: Near the ruins.", rawAssembled[1].content);
        Assert.Equal(RimTalk.Prompt.PromptRole.User, rawAssembled[2].role);
        Assert.Equal("Turn 3: I hear mechanoids.", rawAssembled[2].content);
    }

    [Fact]
    public void ComplexPreset_ModEntryLifecycle_HonorsBlacklistAndDeterministicIds()
    {
        var preset = new RimTalk.Prompt.PromptPreset("ModLifecyclePreset");

        // 1. Mod A adds an entry
        var modEntryA = new RimTalk.Prompt.PromptEntry("Combat Tactics", "Always check line of sight.")
        {
            SourceModId = "CombatMod.PackageId"
        };
        Assert.StartsWith("mod_combatmodpackageid_", modEntryA.Id);

        bool added1 = preset.AddEntry(modEntryA);
        Assert.True(added1);
        Assert.Single(preset.Entries);

        // 2. Duplicate attempt by Mod A is prevented
        var duplicateModEntry = new RimTalk.Prompt.PromptEntry("Combat Tactics", "Different text")
        {
            SourceModId = "CombatMod.PackageId"
        };
        bool added2 = preset.AddEntry(duplicateModEntry);
        Assert.False(added2); // Denied because ID matches existing
        Assert.Single(preset.Entries);

        // 3. User deletes Mod A's entry -> added to blacklist
        bool removed = preset.RemoveEntry(modEntryA.Id);
        Assert.True(removed);
        Assert.Empty(preset.Entries);
        Assert.Contains(modEntryA.Id, preset.DeletedModEntryIds);

        // 4. Next game restart: Mod A tries to register again -> blocked by blacklist
        bool readded = preset.AddEntry(modEntryA);
        Assert.False(readded);
        Assert.Empty(preset.Entries);

        // 5. User resets to defaults -> blacklist cleared
        preset.ClearBlacklist();
        bool allowedAfterReset = preset.AddEntry(modEntryA);
        Assert.True(allowedAfterReset);
        Assert.Single(preset.Entries);
    }

    [Fact]
    public void Scenario_ThirdPartyHook_DynamicInjectionIntoContextCategories()
    {
        // Simulates a third-party mod injecting custom variables (e.g. Combat Extended ammo, Biotech gene details)
        // and verifies it renders properly inside a dynamic category hook
        RimTalk.API.ContextHookRegistry.Clear();

        RimTalk.API.ContextHookRegistry.RegisterPawnVariable("ammo_count", "CombatMod", pawn => "30/30 FMJ", priority: 50);
        RimTalk.API.ContextHookRegistry.RegisterEnvironmentVariable("fallout_level", "ToxicMod", map => "Extreme (0.85)", priority: 50);

        string template = @"
Colonist Ammo: {{ pawn.ammo_count }}
Environment Hazard: {{ fallout_level }}
";

        var templateObj = Template.Parse(template);
        Assert.False(templateObj.HasErrors);

        var scriptObject = new ScriptObject();
        var pawnObj = new ScriptObject();

        var pawn = new Verse.Pawn { Name = "TestColonist" };
        var map = new Verse.Map { uniqueID = 1 };

        if (RimTalk.API.ContextHookRegistry.TryGetPawnVariable("ammo_count", pawn, out var ammoVal))
        {
            pawnObj.Add("ammo_count", ammoVal);
        }

        if (RimTalk.API.ContextHookRegistry.TryGetEnvironmentVariable("fallout_level", map, out var falloutVal))
        {
            scriptObject.Add("fallout_level", falloutVal);
        }

        scriptObject.Add("pawn", pawnObj);

        var context = new TemplateContext();
        context.PushGlobal(scriptObject);

        string rendered = templateObj.Render(context);

        Assert.Contains("Colonist Ammo: 30/30 FMJ", rendered);
        Assert.Contains("Environment Hazard: Extreme (0.85)", rendered);

        RimTalk.API.ContextHookRegistry.Clear();
    }

    [Fact]
    public void BusyGate_DetectsStuckGenerationAndUnblocksPipeline()
    {
        // Tests the backstop watchdog for AI generation:
        // If an API request hangs or network socket is orphaned past 300s,
        // BusyGate must detect it as stuck so the mod unblocks rather than staying frozen forever.
        var startTime = new DateTime(2026, 9, 5, 12, 0, 0);

        // Not busy -> never stuck
        Assert.False(RimTalk.Service.BusyGate.IsStuck(false, startTime, startTime.AddSeconds(400)));

        // Busy, but only 60s elapsed -> normal generation, NOT stuck
        Assert.False(RimTalk.Service.BusyGate.IsStuck(true, startTime, startTime.AddSeconds(60)));

        // Busy past 300s -> STUCK!
        Assert.True(RimTalk.Service.BusyGate.IsStuck(true, startTime, startTime.AddSeconds(301)));
    }

    [Fact]
    public void PresetSerializer_RoundTripExportAndImport_PreservesStructure()
    {
        // Tests preset export and import (JSON DataContract serialization):
        // Ensures custom presets created by users or modders can be exported to JSON and re-imported losslessly.
        var originalPreset = new RimTalk.Prompt.PromptPreset("Combat Preset", "Tactical callouts")
        {
            Entries = new List<RimTalk.Prompt.PromptEntry>
            {
                new("Tactical Header", "Report status under fire.", RimTalk.Prompt.PromptRole.System),
                new("InChat Callout", "Watch flanking angles!", RimTalk.Prompt.PromptRole.User, inChatDepth: 2)
                {
                    Position = RimTalk.Prompt.PromptPosition.InChat
                }
            }
        };

        string exportedJson = RimTalk.Prompt.PresetSerializer.ExportToJson(originalPreset);
        Assert.False(string.IsNullOrWhiteSpace(exportedJson));

        var importedPreset = RimTalk.Prompt.PresetSerializer.ImportFromJson(exportedJson);
        Assert.NotNull(importedPreset);
        Assert.Equal("Combat Preset", importedPreset.Name);
        Assert.Equal("Tactical callouts", importedPreset.Description);
        Assert.Equal(2, importedPreset.Entries.Count);
        Assert.Equal("Tactical Header", importedPreset.Entries[0].Name);
        Assert.Equal(RimTalk.Prompt.PromptRole.System, importedPreset.Entries[0].Role);
        Assert.Equal("InChat Callout", importedPreset.Entries[1].Name);
        Assert.Equal(2, importedPreset.Entries[1].InChatDepth);
        Assert.Equal(RimTalk.Prompt.PromptPosition.InChat, importedPreset.Entries[1].Position);
    }

    [Fact]
    public void VariableStore_CaseInsensitiveStorageAndLookup_WorksReliably()
    {
        // Tests VariableStore handling of cross-entry custom variables
        var store = new RimTalk.Prompt.VariableStore();

        store.SetVar("FactionLeader", "High Stellarch");
        Assert.True(store.HasVar("factionleader"));
        Assert.Equal("High Stellarch", store.GetVar("FACTIONLEADER"));
        Assert.Equal("DefaultVal", store.GetVar("NonExistent", "DefaultVal"));

        store.RemoveVar("factionleader");
        Assert.False(store.HasVar("FactionLeader"));
    }

    [Fact]
    public void BuildSimpleModePreset_IsolatesUserCustomEntries_PreservesAddonAndBuiltInEntries()
    {
        var activePreset = new RimTalk.Prompt.PromptPreset("ActiveCustomPreset");

        // Built-in entries
        var baseInstruction = new RimTalk.Prompt.PromptEntry("Base Instruction", "Original instruction");
        var jsonFormat = new RimTalk.Prompt.PromptEntry("JSON Format", "JSON schema");
        var history = new RimTalk.Prompt.PromptEntry("Chat History", "{{chat.history}}") { IsMainChatHistory = true };
        var prompt = new RimTalk.Prompt.PromptEntry("Dialogue Prompt", "{{prompt}}");

        // User custom entry added in Advanced Mode (SourceModId is null)
        var userCustomEntry = new RimTalk.Prompt.PromptEntry("My User Custom Guideline", "Always talk about potatoes.")
        {
            SourceModId = null
        };

        // Addon mod entry attached by a third-party mod (SourceModId is set)
        var addonEntry = new RimTalk.Prompt.PromptEntry("Combat Addon Reaction", "Check weapon status.")
        {
            SourceModId = "CombatMod.PackageId"
        };

        activePreset.Entries.Add(baseInstruction);
        activePreset.Entries.Add(jsonFormat);
        activePreset.Entries.Add(userCustomEntry);
        activePreset.Entries.Add(addonEntry);
        activePreset.Entries.Add(history);
        activePreset.Entries.Add(prompt);

        string simpleInstruction = "Custom simple instruction for player";
        var simplePreset = RimTalk.Prompt.PromptPresetAssembler.BuildSimpleModePreset(activePreset, simpleInstruction);

        // 1. User custom entry MUST be excluded
        Assert.DoesNotContain(simplePreset.Entries, e => e.Name == "My User Custom Guideline");

        // 2. Addon entry MUST be preserved
        Assert.Contains(simplePreset.Entries, e => e.Name == "Combat Addon Reaction" && e.SourceModId == "CombatMod.PackageId");

        // 3. Base Instruction MUST be overridden with simple instruction
        var effectiveBase = simplePreset.Entries.FirstOrDefault(e => e.Name == "Base Instruction");
        Assert.NotNull(effectiveBase);
        Assert.Equal(simpleInstruction, effectiveBase.Content);

        // 4. Built-in entries (JSON Format, Chat History, Dialogue Prompt) MUST be preserved
        Assert.Contains(simplePreset.Entries, e => e.Name == "JSON Format");
        Assert.Contains(simplePreset.Entries, e => e.IsMainChatHistory);
        Assert.Contains(simplePreset.Entries, e => e.Name == "Dialogue Prompt");

        // 5. Active preset's original Base Instruction content MUST NOT be mutated
        Assert.Equal("Original instruction", baseInstruction.Content);
    }

    [Fact]
    public void BuildSimpleModePreset_SupportsLegacyPawnProfiles_AndForcesEnabledForBuiltIns()
    {
        var activePreset = new RimTalk.Prompt.PromptPreset("LegacyPreset");
        // Legacy config has "Pawn Profiles" instead of "Context", and user disabled some items in Advanced Mode
        var contextEntry = new RimTalk.Prompt.PromptEntry("Pawn Profiles", "{{context}}") { Enabled = false };
        var jsonEntry = new RimTalk.Prompt.PromptEntry("JSON Format", "Output JSON") { Enabled = false };

        activePreset.Entries.Add(contextEntry);
        activePreset.Entries.Add(jsonEntry);

        var simplePreset = RimTalk.Prompt.PromptPresetAssembler.BuildSimpleModePreset(activePreset, "Simple instruction");

        // "Pawn Profiles" must be treated as built-in and preserved
        var resolvedContext = simplePreset.Entries.FirstOrDefault(e => e.Name == "Pawn Profiles");
        Assert.NotNull(resolvedContext);
        // Even if disabled in Advanced mode, built-ins must be forced Enabled = true in Simple mode
        Assert.True(resolvedContext.Enabled);

        var resolvedJson = simplePreset.Entries.FirstOrDefault(e => e.Name == "JSON Format");
        Assert.NotNull(resolvedJson);
        Assert.True(resolvedJson.Enabled);
    }

    [Fact]
    public void BuildSimpleModePreset_WhenAdvancedPresetIsEmpty_InjectsAllFiveEssentialFallbacks()
    {
        // An empty preset in Advanced mode must not crash or fail Simple mode
        var emptyPreset = new RimTalk.Prompt.PromptPreset("EmptyPreset");
        string fallbackJson = "Output JSON.\nRequired keys: 'name', 'text'.";

        var simplePreset = RimTalk.Prompt.PromptPresetAssembler.BuildSimpleModePreset(
            emptyPreset,
            "Simple instruction",
            fallbackInstruction: "Fallback instruction",
            fallbackJsonInstruction: fallbackJson);

        // Essential entries must be present and enabled
        Assert.Contains(simplePreset.Entries, e => e.Name == "Base Instruction" && e.Enabled && e.Content == "Simple instruction");
        Assert.Contains(simplePreset.Entries, e => e.Name == "Context" && e.Enabled && e.Content == "{{context}}");
        Assert.Contains(simplePreset.Entries, e => e.Name == "Dialogue Prompt" && e.Enabled && e.Content == "{{prompt}}");
        Assert.Contains(simplePreset.Entries, e => e.Name == "Chat History" && e.Enabled && e.IsMainChatHistory && e.Content == "{{chat.history}}");
        Assert.Contains(simplePreset.Entries, e => e.Name == "JSON Format" && e.Enabled && e.Content == fallbackJson);

        // Order check: Base Instruction -> JSON Format -> Context -> Recent Events -> Chat History -> Dialogue Prompt
        int baseIdx = simplePreset.Entries.FindIndex(e => e.Name == "Base Instruction");
        int jsonIdx = simplePreset.Entries.FindIndex(e => e.Name == "JSON Format");
        int ctxIdx = simplePreset.Entries.FindIndex(e => e.Name == "Context");
        int eventsIdx = simplePreset.Entries.FindIndex(e => e.Name == "Recent Events");
        int histIdx = simplePreset.Entries.FindIndex(e => e.Name == "Chat History");
        int promptIdx = simplePreset.Entries.FindIndex(e => e.Name == "Dialogue Prompt");

        Assert.True(baseIdx < jsonIdx);
        Assert.True(jsonIdx < ctxIdx);
        Assert.True(ctxIdx < eventsIdx);
        Assert.True(eventsIdx < histIdx);
        Assert.True(histIdx < promptIdx);
        Assert.Equal(RimTalk.Prompt.PromptRole.System, simplePreset.Entries[eventsIdx].Role);
    }

    [Fact]
    public void AssembleMessages_TrailingFormatReminder_AppendedAtVeryBottom()
    {
        var preset = new RimTalk.Prompt.PromptPreset("TestPreset")
        {
            Entries = new List<RimTalk.Prompt.PromptEntry>
            {
                new("Base Instruction", "You are an AI.") { Role = RimTalk.Prompt.PromptRole.System, Position = RimTalk.Prompt.PromptPosition.Relative },
                new("JSON Format", "Output valid JSON only.") { Role = RimTalk.Prompt.PromptRole.System, Position = RimTalk.Prompt.PromptPosition.Relative },
                new("Dialogue Prompt", "Alice speaks to Bob.") { Role = RimTalk.Prompt.PromptRole.User, Position = RimTalk.Prompt.PromptPosition.Relative },
                new("Addon Narrative Context", "Faction relations: Hostile.") { Role = RimTalk.Prompt.PromptRole.User, Position = RimTalk.Prompt.PromptPosition.Relative }
            }
        };

        var segments = new List<RimTalk.Data.PromptMessageSegment>();
        var assembled = RimTalk.Prompt.PromptPresetAssembler.AssembleMessages(
            preset,
            c => c,
            new List<(RimTalk.Data.Role role, string message)>(),
            segments);

        // System message has Base Instruction and JSON Format
        Assert.Equal(RimTalk.Prompt.PromptRole.System, assembled[0].role);
        Assert.Contains("Output valid JSON only.", assembled[0].content);

        // Format reminder must be at the very bottom of the assembled user message with exact format content
        Assert.EndsWith("Output valid JSON only.", assembled[^1].content);
    }


    [Fact]
    public void AssembleMessages_SingleBlockHistory_IncludesContextTriggersNaturally()
    {
        var historyWithTriggers = new List<(RimTalk.Data.Role role, string message)>
        {
            (RimTalk.Data.Role.User, "prompt: Alice continue\nTopic idea: campfire songs\nAlice cooking meal"),
            (RimTalk.Data.Role.AI, "(15s ago) Alice: Sing with me.\n(10s ago) Bob: Not right now."),
            (RimTalk.Data.Role.User, "prompt: Alice initiated: [Insult] directed at Bob"),
            (RimTalk.Data.Role.AI, "(5s ago) Alice: You're always so boring!")
        };

        var defaultPreset = new RimTalk.Prompt.PromptPreset("DefaultPreset");
        defaultPreset.AddEntry(new RimTalk.Prompt.PromptEntry("HistoryMarker", "{{chat.history}}", RimTalk.Prompt.PromptRole.User)
        {
            Position = RimTalk.Prompt.PromptPosition.Relative,
            IsMainChatHistory = true
        });

        var assembled = RimTalk.Prompt.PromptPresetAssembler.AssembleMessages(
            defaultPreset,
            content => content,
            historyWithTriggers);

        Assert.Single(assembled);
        var block = assembled[0].content;
        Assert.StartsWith(RimTalk.Prompt.PromptPresetAssembler.ChatHistoryHeader, block);
        Assert.Contains("prompt: Alice continue", block);
        Assert.Contains("Topic idea: campfire songs", block);
        Assert.Contains("(15s ago) Alice: Sing with me.", block);
        Assert.Contains("prompt: Alice initiated: [Insult] directed at Bob", block);
        Assert.Contains("(5s ago) Alice: You're always so boring!", block);
    }

    [Fact]
    public void ShouldShowHistoryWarning_EvaluatesAccurately()
    {
        // 1. History enabled -> false
        var preset1 = new RimTalk.Prompt.PromptPreset("Test1");
        preset1.AddEntry(new RimTalk.Prompt.PromptEntry("Chat History", "{{chat.history}}")
        {
            IsMainChatHistory = true,
            Enabled = true
        });
        Assert.False(RimTalk.Prompt.PromptPresetAssembler.ShouldShowHistoryWarning(preset1));

        // 2. Main history disabled, but custom entry contains chat.history -> false
        var preset2 = new RimTalk.Prompt.PromptPreset("Test2");
        preset2.AddEntry(new RimTalk.Prompt.PromptEntry("Chat History", "{{chat.history}}")
        {
            IsMainChatHistory = true,
            Enabled = false
        });
        preset2.AddEntry(new RimTalk.Prompt.PromptEntry("Custom History", "Previous context:\n{{chat.history}}")
        {
            Enabled = true
        });
        Assert.False(RimTalk.Prompt.PromptPresetAssembler.ShouldShowHistoryWarning(preset2));

        // 3. Main history disabled, but external addon entry is active -> false
        var preset3 = new RimTalk.Prompt.PromptPreset("Test3");
        preset3.AddEntry(new RimTalk.Prompt.PromptEntry("Chat History", "{{chat.history}}")
        {
            IsMainChatHistory = true,
            Enabled = false
        });
        preset3.AddEntry(new RimTalk.Prompt.PromptEntry("External Addon Memory", "{{addon.memory}}")
        {
            SourceModId = "some.memory.addon",
            Enabled = true
        });
        Assert.False(RimTalk.Prompt.PromptPresetAssembler.ShouldShowHistoryWarning(preset3));

        // 4. Main history disabled, but isExternalMemoryModActive is true -> false
        var preset4 = new RimTalk.Prompt.PromptPreset("Test4");
        preset4.AddEntry(new RimTalk.Prompt.PromptEntry("Chat History", "{{chat.history}}")
        {
            IsMainChatHistory = true,
            Enabled = false
        });
        Assert.False(RimTalk.Prompt.PromptPresetAssembler.ShouldShowHistoryWarning(preset4, isExternalMemoryModActive: true));

        // 5. Main history disabled, no custom history, no addon -> true
        var preset5 = new RimTalk.Prompt.PromptPreset("Test5");
        preset5.AddEntry(new RimTalk.Prompt.PromptEntry("Chat History", "{{chat.history}}")
        {
            IsMainChatHistory = true,
            Enabled = false
        });
        preset5.AddEntry(new RimTalk.Prompt.PromptEntry("Some Other Entry", "Hello world")
        {
            Enabled = true
        });
        Assert.True(RimTalk.Prompt.PromptPresetAssembler.ShouldShowHistoryWarning(preset5));
    }

    [Fact]
    public void Scenario_UseCompactHistory_TogglesBetweenSingleBlockAndMultiTurn()
    {
        var preset = new RimTalk.Prompt.PromptPreset("TestCompactHistory");
        preset.AddEntry(new RimTalk.Prompt.PromptEntry("Base Instruction", "System instruction.")
        {
            Position = RimTalk.Prompt.PromptPosition.Relative,
            Role = RimTalk.Prompt.PromptRole.System
        });
        preset.AddEntry(new RimTalk.Prompt.PromptEntry("Chat History", "{{chat.history}}")
        {
            IsMainChatHistory = true,
            Position = RimTalk.Prompt.PromptPosition.Relative,
            Role = RimTalk.Prompt.PromptRole.User
        });
        preset.AddEntry(new RimTalk.Prompt.PromptEntry("Dialogue Prompt", "Current Prompt")
        {
            Position = RimTalk.Prompt.PromptPosition.Relative,
            Role = RimTalk.Prompt.PromptRole.User
        });

        const string rawJson1 = "[{\"name\": \"ColonistA\", \"text\": \"Hello!\"}]";
        const string rawJson2 = "[{\"name\": \"ColonistB\", \"text\": \"Greetings!\"}]";

        var chatHistory = new List<(RimTalk.Data.Role role, string message)>
        {
            (RimTalk.Data.Role.User, "prompt: Chat"),
            (RimTalk.Data.Role.AI, rawJson1),
            (RimTalk.Data.Role.User, "prompt: Reply"),
            (RimTalk.Data.Role.AI, rawJson2)
        };

        // 1. When useCompact is true (default ON): groups history into single block
        var compactMessages = RimTalk.Prompt.PromptPresetAssembler.AssembleMessages(
            preset,
            content => content,
            chatHistory);

        Assert.Contains(compactMessages, m => m.content.Contains("[Chat History]") && m.content.Contains(rawJson1));
        Assert.DoesNotContain(compactMessages, m => m.role == RimTalk.Prompt.PromptRole.Assistant);

        // 2. When useCompact is false (OFF): decoupled legacy multi-turn alternating messages
        var multiTurnMessages = RimTalk.Prompt.LegacyMultiTurnPromptBuilder.AssembleMessages(
            preset,
            content => content,
            chatHistory);

        // System message first
        Assert.Equal(RimTalk.Prompt.PromptRole.System, multiTurnMessages[0].role);
        Assert.Equal("System instruction.", multiTurnMessages[0].content);

        // History turns strictly alternating with full raw JSON
        Assert.Equal(RimTalk.Prompt.PromptRole.User, multiTurnMessages[1].role);
        Assert.Equal("prompt: Chat", multiTurnMessages[1].content);

        Assert.Equal(RimTalk.Prompt.PromptRole.Assistant, multiTurnMessages[2].role);
        Assert.Equal(rawJson1, multiTurnMessages[2].content);

        Assert.Equal(RimTalk.Prompt.PromptRole.User, multiTurnMessages[3].role);
        Assert.Equal("prompt: Reply", multiTurnMessages[3].content);

        Assert.Equal(RimTalk.Prompt.PromptRole.Assistant, multiTurnMessages[4].role);
        Assert.Equal(rawJson2, multiTurnMessages[4].content);

        // Final turn: current Dialogue Prompt as User
        Assert.Equal(RimTalk.Prompt.PromptRole.User, multiTurnMessages[5].role);
        Assert.Equal("Current Prompt", multiTurnMessages[5].content);

        Assert.DoesNotContain(multiTurnMessages, m => m.content.Contains("[Chat History]"));

        // Verify strictly alternating non-consecutive roles after system
        for (int i = 1; i < multiTurnMessages.Count - 1; i++)
        {
            Assert.NotEqual(multiTurnMessages[i].role, multiTurnMessages[i + 1].role);
        }
    }

    [Fact]
    public void LegacyMultiTurnPromptBuilder_NormalizesMalformedHistory()
    {
        var preset = new RimTalk.Prompt.PromptPreset("TestMalformedHistory");
        preset.AddEntry(new RimTalk.Prompt.PromptEntry("Chat History", "{{chat.history}}")
        {
            IsMainChatHistory = true,
            Position = RimTalk.Prompt.PromptPosition.Relative,
            Role = RimTalk.Prompt.PromptRole.User
        });
        preset.AddEntry(new RimTalk.Prompt.PromptEntry("Dialogue Prompt", "Current Prompt")
        {
            Position = RimTalk.Prompt.PromptPosition.Relative,
            Role = RimTalk.Prompt.PromptRole.User
        });

        // History with leading orphaned AI message and trailing orphaned User message
        var malformedHistory = new List<(RimTalk.Data.Role role, string message)>
        {
            (RimTalk.Data.Role.AI, "[{\"name\": \"Old\", \"text\": \"Orphaned AI\"}]"),
            (RimTalk.Data.Role.User, "prompt: Valid prompt"),
            (RimTalk.Data.Role.AI, "[{\"name\": \"Bob\", \"text\": \"Valid response\"}]"),
            (RimTalk.Data.Role.User, "prompt: Orphaned trailing prompt")
        };

        var messages = RimTalk.Prompt.LegacyMultiTurnPromptBuilder.AssembleMessages(
            preset,
            content => content,
            malformedHistory);

        // Should ignore leading orphaned AI and trailing orphaned User, keeping User -> Assistant -> User(Prompt)
        Assert.Equal(3, messages.Count);
        Assert.Equal(RimTalk.Prompt.PromptRole.User, messages[0].role);
        Assert.Equal("prompt: Valid prompt", messages[0].content);
        Assert.Equal(RimTalk.Prompt.PromptRole.Assistant, messages[1].role);
        Assert.Equal("[{\"name\": \"Bob\", \"text\": \"Valid response\"}]", messages[1].content);
        Assert.Equal(RimTalk.Prompt.PromptRole.User, messages[2].role);
        Assert.Equal("Current Prompt", messages[2].content);
    }
}
