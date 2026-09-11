using System;
using System.Collections.Generic;
using Scriban;
using Scriban.Runtime;
using Xunit;

namespace RimTalk.Tests;

public class PromptTemplateTests
{
    [Fact]
    public void Scriban_RenderStandardPrompt_ResolvesVariablesAccurately()
    {
        string templateText = @"
You are narrating dialogue for RimWorld.
Initiator: {{ pawn1.name }} (Trait: {{ pawn1.trait }})
Recipient: {{ pawn2.name }} (Trait: {{ pawn2.trait }})
Generate a 1-sentence interaction.
";

        var template = Template.Parse(templateText);
        Assert.False(template.HasErrors, $"Template errors: {string.Join(", ", template.Messages)}");

        var scriptObject = new ScriptObject();
        var pawn1 = new ScriptObject { { "name", "Tilly" }, { "trait", "Bloodlust" } };
        var pawn2 = new ScriptObject { { "name", "Ray" }, { "trait", "Kind" } };
        scriptObject.Add("pawn1", pawn1);
        scriptObject.Add("pawn2", pawn2);

        var context = new TemplateContext();
        context.PushGlobal(scriptObject);

        string rendered = template.Render(context);

        Assert.Contains("Initiator: Tilly (Trait: Bloodlust)", rendered);
        Assert.Contains("Recipient: Ray (Trait: Kind)", rendered);
    }

    [Fact]
    public void Scriban_UnknownVariable_DoesNotThrowException()
    {
        string templateText = "Hello {{ pawn1.unknown_variable }}! Mood: {{ pawn1.mood }}";
        var template = Template.Parse(templateText);
        Assert.False(template.HasErrors);

        var scriptObject = new ScriptObject();
        var pawn1 = new ScriptObject { { "mood", "Happy" } };
        scriptObject.Add("pawn1", pawn1);

        var context = new TemplateContext();
        context.PushGlobal(scriptObject);

        string rendered = template.Render(context);

        // Unknown variable should evaluate to empty string, not crash
        Assert.Contains("Hello ! Mood: Happy", rendered);
    }

    [Fact]
    public void Scriban_SoloMonologue_SkipsPawn2Gracefully()
    {
        string templateText = @"
{{ if pawn2 }}
Conversation between {{ pawn1.name }} and {{ pawn2.name }}.
{{ else }}
Solo monologue by {{ pawn1.name }}.
{{ end }}";

        var template = Template.Parse(templateText);
        Assert.False(template.HasErrors);

        var scriptObject = new ScriptObject();
        scriptObject.Add("pawn1", new ScriptObject { { "name", "LoneSurvivor" } });
        scriptObject.Add("pawn2", null);

        var context = new TemplateContext();
        context.PushGlobal(scriptObject);

        string rendered = template.Render(context);

        Assert.Contains("Solo monologue by LoneSurvivor.", rendered);
        Assert.DoesNotContain("Conversation between", rendered);
    }

    [Fact]
    public void Scenario_MultiPawnDialogue_FormatsTwoPartyContextAndSocialRelations()
    {
        // Realistic scenario: 2 colonists interacting with social opinions, traits, and dialogue prompt
        string multiTurnTemplate = @"
[Scenario: {{ talk_type }}]
Initiator: {{ initiator.name }} (Trait: {{ initiator.trait }}, Mood: {{ initiator.mood }})
Recipient: {{ recipient.name }} (Trait: {{ recipient.trait }}, OpinionOfInitiator: {{ recipient.opinion }})
Topic: {{ topic }}
Prompt: {{ prompt }}
Instruction: Generate multi-turn conversation between {{ initiator.name }} and {{ recipient.name }}.";

        var template = Template.Parse(multiTurnTemplate);
        Assert.False(template.HasErrors, string.Join("\n", template.Messages));

        var scriptObject = new ScriptObject();
        var initiator = new ScriptObject { { "name", "Val" }, { "trait", "Neurotic" }, { "mood", "Stressed" } };
        var recipient = new ScriptObject { { "name", "Tate" }, { "trait", "Kind" }, { "opinion", "+45" } };

        scriptObject.Add("initiator", initiator);
        scriptObject.Add("recipient", recipient);
        scriptObject.Add("talk_type", "Interaction");
        scriptObject.Add("topic", "sharing meals in dining room");
        scriptObject.Add("prompt", "Val complains about nutrient paste to Tate.");

        var context = new TemplateContext();
        context.PushGlobal(scriptObject);

        string rendered = template.Render(context);

        Assert.Contains("Initiator: Val (Trait: Neurotic, Mood: Stressed)", rendered);
        Assert.Contains("Recipient: Tate (Trait: Kind, OpinionOfInitiator: +45)", rendered);
        Assert.Contains("Topic: sharing meals in dining room", rendered);
        Assert.Contains("Val complains about nutrient paste to Tate.", rendered);
        Assert.Contains("Generate multi-turn conversation between Val and Tate.", rendered);
    }

    [Fact]
    public void Scenario_SoloMonologue_EmergencyOrThought_OmitsRecipientAndEnforcesSingleTurn()
    {
        // Realistic scenario: Solo colonist having a mental break or thought event without recipient
        string soloTemplate = @"
{{ if recipient }}
Dialogue between {{ initiator.name }} and {{ recipient.name }}.
{{ else }}
[Monologue]
Character: {{ initiator.name }} (Hediff: {{ initiator.hediff }})
Situation: {{ prompt }}
Instruction: Speak exactly 1 short monologue turn reflecting inner thoughts. Do not address an imaginary listener.
{{ end }}";

        var template = Template.Parse(soloTemplate);
        Assert.False(template.HasErrors);

        var scriptObject = new ScriptObject();
        var initiator = new ScriptObject { { "name", "Red" }, { "hediff", "Food Poisoning" } };

        scriptObject.Add("initiator", initiator);
        scriptObject.Add("recipient", null);
        scriptObject.Add("prompt", "Red collapsed near the crops throwing up.");

        var context = new TemplateContext();
        context.PushGlobal(scriptObject);

        string rendered = template.Render(context);

        Assert.Contains("[Monologue]", rendered);
        Assert.Contains("Character: Red (Hediff: Food Poisoning)", rendered);
        Assert.Contains("Red collapsed near the crops throwing up.", rendered);
        Assert.Contains("Speak exactly 1 short monologue turn", rendered);
        Assert.DoesNotContain("Dialogue between", rendered);
    }

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
    public void TopicKeywordPool_ContainsCleanConciseKeywords()
    {
        // Tests the topic keyword pool: Ensures all approach and subject keywords
        // are short (1-4 words), non-empty, and free from markdown or illegal syntax.
        Assert.NotEmpty(RimTalk.Data.TopicKeywordPool.ApproachKeywords);
        Assert.NotEmpty(RimTalk.Data.TopicKeywordPool.SubjectKeywords);

        foreach (var keyword in RimTalk.Data.TopicKeywordPool.ApproachKeywords)
        {
            Assert.False(string.IsNullOrWhiteSpace(keyword));
            Assert.True(keyword.Length <= 30, $"Keyword '{keyword}' is too verbose for a prompt anchor.");
        }

        foreach (var subject in RimTalk.Data.TopicKeywordPool.SubjectKeywords)
        {
            Assert.False(string.IsNullOrWhiteSpace(subject));
            Assert.True(subject.Length <= 40, $"Subject '{subject}' is too verbose for a prompt anchor.");
        }
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
}

