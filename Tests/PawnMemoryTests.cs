using System.Collections.Generic;
using RimTalk.PawnMemory;
using Xunit;

namespace RimTalk.Tests;

public class PawnMemoryTests
{
    private const int TicksPerDay = 60000;

    [Fact]
    public void MemoryEntry_EbbinghausDecay_CalculatesCorrectly()
    {
        int createdTick = 100000;
        var entry = new MemoryEntry(1, "Bob", "Rescued", 80f, createdTick, "rescued from danger");

        // At creation tick: no decay
        float initialWeight = entry.GetDecayedWeight(createdTick, 4f);
        Assert.Equal(80f, initialWeight, precision: 2);

        // After 4 days (half-life): weight should be halved to 40
        int fourDaysLater = createdTick + (4 * TicksPerDay);
        float halfLifeWeight = entry.GetDecayedWeight(fourDaysLater, 4f);
        Assert.Equal(40f, halfLifeWeight, precision: 1);

        // After 8 days (two half-lives): weight should be quartered to 20
        int eightDaysLater = createdTick + (8 * TicksPerDay);
        float twoHalfLivesWeight = entry.GetDecayedWeight(eightDaysLater, 4f);
        Assert.Equal(20f, twoHalfLivesWeight, precision: 1);
    }

    [Fact]
    public void MemoryEntry_DynamicHalfLife_CalibratesByEmotionalSignificance()
    {
        int tick = 100000;

        // Mild memory (15f): 4-day half-life
        var mild = new MemoryEntry(1, "Bob", "Tended", 15f, tick, "tended wounds");
        Assert.Equal(4f, mild.GetDynamicHalfLifeDays());
        float mildAfter4Days = mild.GetDecayedWeight(tick + 4 * 60000);
        Assert.Equal(7.5f, mildAfter4Days, precision: 1);

        // Moderate memory (40f): 8-day half-life
        var moderate = new MemoryEntry(1, "Bob", "SocialFight", -40f, tick, "brawl");
        Assert.Equal(8f, moderate.GetDynamicHalfLifeDays());
        float moderateAfter8Days = moderate.GetDecayedWeight(tick + 8 * 60000);
        Assert.Equal(-20f, moderateAfter8Days, precision: 1);

        // Major memory (80f): 14-day half-life
        var major = new MemoryEntry(1, "Bob", "SavedLife", 80f, tick, "saved life");
        Assert.Equal(14f, major.GetDynamicHalfLifeDays());
        float majorAfter14Days = major.GetDecayedWeight(tick + 14 * 60000);
        Assert.Equal(40f, majorAfter14Days, precision: 1);

        // Core trauma (any weight): 7-day half-life
        var trauma = new MemoryEntry(1, "Bob", "Trauma", -85f, tick, "grief", isDirective: false, isCoreTrauma: true);
        Assert.Equal(7f, trauma.GetDynamicHalfLifeDays());
        float traumaAfter7Days = trauma.GetDecayedWeight(tick + 7 * 60000);
        Assert.Equal(-42.5f, traumaAfter7Days, precision: 1);
    }

    [Fact]
    public void MemoryEntry_RecencyFatigue_SuppressesRecentRecalls()
    {
        int currentTick = 200000;
        var entry = new MemoryEntry(1, "Bob", "SocialFight", -50f, currentTick, "fistfight");

        // Never recalled: full score
        float normalScore = entry.GetRecallScore(currentTick, 4f);
        Assert.Equal(-50f, normalScore, precision: 2);

        // Recalled just now (within 1 in-game hour): suppressed by 80% (0.2x)
        entry.LastRecalledTick = currentTick - 1000;
        float recentRecallScore = entry.GetRecallScore(currentTick, 4f);
        Assert.Equal(-10f, recentRecallScore, precision: 1);

        // Recalled 3 hours ago: suppressed by 50% (0.5x)
        entry.LastRecalledTick = currentTick - 7500;
        float moderateRecallScore = entry.GetRecallScore(currentTick, 4f);
        Assert.Equal(-25f, moderateRecallScore, precision: 1);
    }

    [Fact]
    public void PawnMemoryTracker_Debounce_CombinesRapidRepeats()
    {
        var list = new List<MemoryEntry>();
        int startTick = 10000;

        // First event
        PawnMemoryTracker.AddOrUpdateMemory(list, 2, "Alice", "Insulted", -20f, "insulted", startTick);
        Assert.Single(list);
        Assert.Equal(-20f, list[0].BaseWeight);

        // Rapid repeat within debounce window (e.g. after 2000 ticks)
        PawnMemoryTracker.AddOrUpdateMemory(list, 2, "Alice", "Insulted", -20f, "insulted repeatedly", startTick + 2000);
        Assert.Single(list); // No duplicate entry created
        Assert.Equal(-28f, list[0].BaseWeight); // Blended: -20 + (-20 * 0.4)
        Assert.Equal("insulted repeatedly", list[0].Note);
    }

    [Fact]
    public void PawnMemoryTracker_SelectTopMemories_CapturesAmbivalence()
    {
        var list = new List<MemoryEntry>();
        int currentTick = 500000;

        // Add positive and negative memories toward Bob (id 1)
        PawnMemoryTracker.AddOrUpdateMemory(list, 1, "Bob", "Rescued", 70f, "saved my life", currentTick);
        PawnMemoryTracker.AddOrUpdateMemory(list, 1, "Bob", "Insulted", -40f, "insulted my shooting", currentTick);

        // Add unrelated memory toward Charlie (id 2)
        PawnMemoryTracker.AddOrUpdateMemory(list, 2, "Charlie", "Compliment", 30f, "kind words", currentTick);

        var (pos, neg) = PawnMemoryTracker.SelectTopMemories(list, 1, currentTick);

        Assert.NotNull(pos);
        Assert.Equal("Rescued", pos.EventKey);
        Assert.NotNull(neg);
        Assert.Equal("Insulted", neg.EventKey);

        // Format into memory header
        string impression = MemoryFormatter.FormatImpression("Bob", pos, neg);
        Assert.Contains("Memory with Bob:", impression);
        Assert.Contains("saved my life", impression);
        Assert.Contains("insulted my shooting", impression);
        Assert.DoesNotContain("Conflicted feelings", impression);
        Assert.DoesNotContain("Do not quote memories directly", impression);

        // Test SelectTopRecallMemories
        var top = PawnMemoryTracker.SelectTopRecallMemories(list, 1, currentTick, maxMemories: 2);
        Assert.Equal(2, top.Count);
        Assert.Equal("Rescued", top[0].EventKey); // 70f > 40f
        Assert.Equal("Insulted", top[1].EventKey);
    }

    [Fact]
    public void PawnMemoryTracker_MaxCap_PrunesWeakestDecayedEntries()
    {
        var list = new List<MemoryEntry>();
        int currentTick = 1000000;

        // Fill memories up to max capacity
        for (int i = 0; i < PawnMemoryTracker.MaxMemoriesPerPawn; i++)
        {
            PawnMemoryTracker.AddOrUpdateMemory(list, 10 + i, $"Colonist{i}", $"Event{i}", 30f, "something", currentTick);
        }
        Assert.Equal(PawnMemoryTracker.MaxMemoriesPerPawn, list.Count);

        // Make the first entry very old so its weight decays close to zero
        list[0].CreatedTick = currentTick - (50 * TicksPerDay); // 50 days old

        // Add one more new important memory
        PawnMemoryTracker.AddOrUpdateMemory(list, 999, "Hero", "EpicDeed", 90f, "heroic act", currentTick);

        // Total count should remain capped at MaxMemoriesPerPawn
        Assert.Equal(PawnMemoryTracker.MaxMemoriesPerPawn, list.Count);
        // The heavily decayed first entry should have been pruned
        Assert.DoesNotContain(list, m => m.TargetPawnId == 10);
        // The new memory must be present
        Assert.Contains(list, m => m.TargetPawnId == 999);
    }

    [Fact]
    public void PawnMemoryTracker_CoreTrauma_SelectsSevereMourning()
    {
        var list = new List<MemoryEntry>();
        int currentTick = 200000;

        PawnMemoryTracker.AddOrUpdateMemory(list, -1, string.Empty, "MySpouseDied", -85f, "mourning the tragic loss of my spouse", currentTick, isCoreTrauma: true);

        var trauma = PawnMemoryTracker.SelectCoreTrauma(list, currentTick);
        Assert.NotNull(trauma);
        Assert.True(trauma.IsCoreTrauma);

        string formatted = MemoryFormatter.FormatCoreTrauma(trauma);
        Assert.Contains("Deeply burdened by mourning the tragic loss of my spouse", formatted);
        Assert.Contains("subdued", formatted);
    }

    [Fact]
    public void PawnMemoryTracker_Directive_SelectsLatestPlayerOrder()
    {
        var list = new List<MemoryEntry>();
        int startTick = 10000;

        // Initial order
        PawnMemoryTracker.AddOrUpdateMemory(list, -999, "Player", "PlayerDirective", 90f, "Avoid interacting with Bob", startTick, isDirective: true);

        // Later updated order
        PawnMemoryTracker.AddOrUpdateMemory(list, -999, "Player", "PlayerDirective", 90f, "Reconcile with Bob and stay close", startTick + 50000, isDirective: true);

        var activeDirective = PawnMemoryTracker.SelectActiveDirective(list, startTick + 50000);
        Assert.NotNull(activeDirective);
        Assert.True(activeDirective.IsDirective);
        Assert.Equal("Reconcile with Bob and stay close", activeDirective.Note);

        string formatted = MemoryFormatter.FormatDirective(activeDirective);
        Assert.Contains("Active Directive", formatted);
        Assert.Contains("Reconcile with Bob and stay close", formatted);
    }

    [Fact]
    public void PawnMemoryTracker_CoreTraumas_CombinesMultipleSevereLosses()
    {
        var list = new List<MemoryEntry>();
        int currentTick = 200000;

        PawnMemoryTracker.AddOrUpdateMemory(list, -1, string.Empty, "MySpouseDied", -85f, "mourning the loss of spouse Bob", currentTick, isCoreTrauma: true);
        PawnMemoryTracker.AddOrUpdateMemory(list, -2, string.Empty, "MyChildDied", -85f, "mourning the loss of child Jack", currentTick, isCoreTrauma: true);

        var traumas = PawnMemoryTracker.SelectCoreTraumas(list, currentTick);
        Assert.Equal(2, traumas.Count);

        string formatted = MemoryFormatter.FormatCoreTraumas(traumas);
        Assert.Contains("mourning the loss of spouse Bob", formatted);
        Assert.Contains("mourning the loss of child Jack", formatted);
        Assert.Contains("grieving", formatted);
    }

    [Fact]
    public void PawnMemoryTracker_CoreTraumas_DeduplicatesSameSubjectIndividual()
    {
        var list = new List<MemoryEntry>();
        int currentTick = 200000;

        // Killing guilt (-88) and bereavement trauma (-74) for the same individual (Allison)
        PawnMemoryTracker.AddOrUpdateMemory(list, 101, "Allison", "KilledColonist", -88f, "haunting guilt over killing fellow colonist Allison", currentTick, isCoreTrauma: true);
        PawnMemoryTracker.AddOrUpdateMemory(list, 101, "Allison", "MyLoverDied", -74f, "grief and trauma from lover died (Allison)", currentTick, isCoreTrauma: true);
        // Another colonist death (Kior)
        PawnMemoryTracker.AddOrUpdateMemory(list, 102, "Kior", "KilledColonist", -70f, "haunting guilt over killing fellow colonist Kior", currentTick, isCoreTrauma: true);

        var traumas = PawnMemoryTracker.SelectCoreTraumas(list, currentTick);

        // Should only select 2 entries: the single most severe trauma for Allison (-88), and Kior's trauma (-70)
        Assert.Equal(2, traumas.Count);
        Assert.Equal("KilledColonist", traumas[0].EventKey);
        Assert.Equal(101, traumas[0].TargetPawnId);
        Assert.Equal("KilledColonist", traumas[1].EventKey);
        Assert.Equal(102, traumas[1].TargetPawnId);
    }

    [Fact]
    public void PawnMemoryTracker_Directives_SupportsMultiBulletList()
    {
        var list = new List<MemoryEntry>();
        int currentTick = 10000;

        PawnMemoryTracker.AddOrUpdateMemory(list, -999, "Player", "PlayerDirective_0", 90f, "Wear flak vest outside", currentTick, isDirective: true);
        PawnMemoryTracker.AddOrUpdateMemory(list, -999, "Player", "PlayerDirective_1", 90f, "Treat Bob kindly", currentTick + 1000, isDirective: true);

        var directives = PawnMemoryTracker.SelectActiveDirectives(list, currentTick + 1000);
        Assert.Equal(2, directives.Count);

        string formatted = MemoryFormatter.FormatDirectives(directives);
        Assert.Contains("Active Directives", formatted);
        Assert.Contains("- Wear flak vest outside", formatted);
        Assert.Contains("- Treat Bob kindly", formatted);
    }

    [Fact]
    public void PawnMemoryTracker_CapacitySeparation_DirectivesAndTraumasImmuneToOrdinaryPruning()
    {
        var list = new List<MemoryEntry>();
        int currentTick = 1000000;

        // Add 1 directive and 1 core trauma
        PawnMemoryTracker.AddOrUpdateMemory(list, -999, "Player", "PlayerDirective_0", 90f, "Guard the northern wall", currentTick, isDirective: true);
        PawnMemoryTracker.AddOrUpdateMemory(list, -1, string.Empty, "MySpouseDied", -85f, "mourning spouse", currentTick, isCoreTrauma: true);

        // Fill memories with 25 ordinary entries
        for (int i = 0; i < PawnMemoryTracker.MaxMemoriesPerPawn; i++)
        {
            PawnMemoryTracker.AddOrUpdateMemory(list, 10 + i, $"Colonist{i}", $"Event{i}", 30f, "ordinary event", currentTick);
        }

        // Add one more ordinary memory that triggers pruning
        PawnMemoryTracker.AddOrUpdateMemory(list, 999, "NewPawn", "NewEvent", 40f, "new ordinary", currentTick);

        // Directive and trauma must NEVER be pruned by ordinary memory capacity overflow
        Assert.Contains(list, m => m.IsDirective && m.EventKey == "PlayerDirective_0");
        Assert.Contains(list, m => m.IsCoreTrauma && m.EventKey == "MySpouseDied");

        // Ordinary memories count should remain exactly MaxMemoriesPerPawn (25)
        int ordinaryCount = list.FindAll(m => !m.IsDirective && !m.IsCoreTrauma).Count;
        Assert.Equal(PawnMemoryTracker.MaxMemoriesPerPawn, ordinaryCount);
    }

    [Fact]
    public void PawnMemoryTracker_Directives_PersistWithoutPassiveTimeDecay()
    {
        var list = new List<MemoryEntry>();
        int createdTick = 10000;
        PawnMemoryTracker.AddOrUpdateMemory(list, -999, "Player", "PlayerDirective_0", 90f, "Do not harvest organs", createdTick, isDirective: true);

        // 100 in-game days later (6,000,000 ticks)
        int hundredDaysLater = createdTick + (100 * TicksPerDay);
        var activeDirectives = PawnMemoryTracker.SelectActiveDirectives(list, hundredDaysLater);

        Assert.Single(activeDirectives);
        Assert.Equal("Do not harvest organs", activeDirectives[0].Note);
    }

    [Fact]
    public void MemoryHistory_AddAndEvict_LogsTransitionsProperly()
    {
        MemoryHistory.Clear();
        var list = new List<MemoryEntry>();
        int tick = 50000;

        // 1. Add episodic memory
        PawnMemoryTracker.AddOrUpdateMemory("ColonistA", 1, list, 2, "ColonistB", "Rescued", 70f, "rescued", tick);
        var logs = MemoryHistory.GetAll();
        Assert.NotEmpty(logs);
        var firstLog = logs[logs.Count - 1];
        Assert.Equal("ColonistA", firstLog.SourcePawnName);
        Assert.Equal("ColonistB", firstLog.TargetPawnName);
        Assert.Equal(MemoryChangeType.Added, firstLog.ChangeType);
        Assert.Equal(70f, firstLog.NewWeight);

        // 2. Debounce repeat
        PawnMemoryTracker.AddOrUpdateMemory("ColonistA", 1, list, 2, "ColonistB", "Rescued", 50f, "rescued again", tick + 1000);
        logs = MemoryHistory.GetAll();
        var debouncedLog = logs[logs.Count - 1];
        Assert.Equal(MemoryChangeType.Updated, debouncedLog.ChangeType);
        Assert.Equal(70f, debouncedLog.OldWeight);
        Assert.Equal(90f, debouncedLog.NewWeight);
    }

    [Fact]
    public void PawnMemoryTracker_RoutineConversations_PurgedOnConsolidation()
    {
        var list = new List<MemoryEntry>
        {
            new MemoryEntry(1, "Bob", "Chitchat", 10f, 1000, "had positive experience (chitchat)"),
            new MemoryEntry(1, "Bob", "DeepTalk", 38f, 1000, "had positive experience (deep talk)"),
            new MemoryEntry(1, "Bob", "RescuedMe", 38f, 1000, "rescued me")
        };

        // Purge routine conversations
        PawnMemoryTracker.PurgeDecayedMemories(list, 1000);

        // Chitchat and DeepTalk should be purged, only RescuedMe remains
        Assert.Single(list);
        Assert.Equal("RescuedMe", list[0].EventKey);

        // Test StripBoilerplate
        Assert.Equal("chitchat", PawnMemoryTracker.StripBoilerplate("had positive experience (chitchat)"));
        Assert.Equal("hurt me", PawnMemoryTracker.StripBoilerplate("had grievance or conflict (hurt me)"));
        Assert.Equal("crashed together", PawnMemoryTracker.StripBoilerplate("crashed together"));
    }

    [Fact]
    public void PawnMemoryTracker_KillTrauma_FormatsKillerMindsetCorrectly()
    {
        // 1. Callous / bloodlust killer (positive weight)
        var callousTrauma = new MemoryEntry(-1, string.Empty, "KilledColonist", 35f, 1000, "killed fellow colonist Allison without remorse", isCoreTrauma: true);
        string callousFormatted = MemoryFormatter.FormatCoreTrauma(callousTrauma);
        Assert.Contains("Marked by killed fellow colonist Allison without remorse", callousFormatted);
        Assert.Contains("callous, remorseless", callousFormatted);
        Assert.DoesNotContain("subdued", callousFormatted);

        // 2. Normal colonist with guilt (negative weight kill event)
        var guiltTrauma = new MemoryEntry(-1, string.Empty, "KilledColonist", -75f, 1000, "haunting guilt over killing fellow colonist Allison", isCoreTrauma: true);
        string guiltFormatted = MemoryFormatter.FormatCoreTrauma(guiltTrauma);
        Assert.Contains("Marked by haunting guilt over killing fellow colonist Allison", guiltFormatted);
        Assert.Contains("guilty, defensive", guiltFormatted);
        Assert.DoesNotContain("subdued", guiltFormatted);
    }

    [Fact]
    public void Milestone_NotPurgedByTimeDecay()
    {
        var list = new List<MemoryEntry>();
        int createdTick = 100000;
        // Normal memory vs Milestone
        PawnMemoryTracker.AddOrUpdateMemory("Alice", 1, list, 2, "Bob", "TendedMe", 15f, "tended wounds", createdTick, isDirective: false, isCoreTrauma: false, isMilestone: false);
        PawnMemoryTracker.AddOrUpdateMemory("Alice", 1, list, 2, "Bob", "Marriage", 80f, "married in joyous ceremony", createdTick, isDirective: false, isCoreTrauma: false, isMilestone: true);

        Assert.Equal(2, list.Count);

        // After 60 in-game days (1 full RimWorld year = 3,600,000 ticks)
        int oneYearLater = createdTick + 3600000;
        PawnMemoryTracker.PurgeDecayedMemories(list, oneYearLater);

        // Ordinary memory should be purged, but Milestone must remain intact!
        Assert.Single(list);
        Assert.True(list[0].IsMilestone);
        Assert.Equal("Marriage", list[0].EventKey);
        Assert.Equal(80f, list[0].GetDecayedWeight(oneYearLater));
    }

    [Fact]
    public void Milestone_MultiSlotCapacity_AllowsUpToThree_AndEvictsWeakest()
    {
        var list = new List<MemoryEntry>();
        int tick = 100000;

        // 1st milestone: BecameLover (+65f)
        PawnMemoryTracker.AddOrUpdateMemory("Alice", 1, list, 2, "Bob", "BecameLover", 65f, "became lovers", tick, isDirective: false, isCoreTrauma: false, isMilestone: true);
        Assert.Single(list);
        Assert.Equal("BecameLover", list[0].EventKey);

        // 2nd milestone: Marriage (+80f) -> Both are preserved!
        PawnMemoryTracker.AddOrUpdateMemory("Alice", 1, list, 2, "Bob", "Marriage", 80f, "married in joyous ceremony", tick + 5000, isDirective: false, isCoreTrauma: false, isMilestone: true);
        Assert.Equal(2, list.Count);

        // 3rd milestone: GaveBirthWith (+85f) -> All three are preserved!
        PawnMemoryTracker.AddOrUpdateMemory("Alice", 1, list, 2, "Bob", "GaveBirthWith", 85f, "welcomed child into world with Bob", tick + 10000, isDirective: false, isCoreTrauma: false, isMilestone: true);
        Assert.Equal(3, list.Count);

        // 4th milestone: MurderedKin (-95f) -> Exceeds MaxMilestonesPerTarget (3), evicts the lowest significance (BecameLover 65f)
        PawnMemoryTracker.AddOrUpdateMemory("Alice", 1, list, 2, "Bob", "MurderedKin", -95f, "murdered kin", tick + 15000, isDirective: false, isCoreTrauma: false, isMilestone: true);
        Assert.Equal(3, list.Count);
        Assert.DoesNotContain(list, m => m.EventKey == "BecameLover");
        Assert.Contains(list, m => m.EventKey == "Marriage");
        Assert.Contains(list, m => m.EventKey == "GaveBirthWith");
        Assert.Contains(list, m => m.EventKey == "MurderedKin");
    }

    [Fact]
    public void Milestone_SelectMilestone_SuppressedWithin1DayCooldown()
    {
        var list = new List<MemoryEntry>();
        int tick = 100000;
        var milestone = new MemoryEntry(2, "Bob", "Marriage", 80f, tick, "married in joyous ceremony", false, false, true);
        list.Add(milestone);

        // First recall: eligible
        var selected = PawnMemoryTracker.SelectMilestone(list, 2, tick, cooldownTicks: 60000);
        Assert.NotNull(selected);

        // Mark recalled
        PawnMemoryTracker.MarkRecalled(selected, tick);

        // Same day (e.g. 10,000 ticks later): suppressed by cooldown
        var suppressed = PawnMemoryTracker.SelectMilestone(list, 2, tick + 10000, cooldownTicks: 60000);
        Assert.Null(suppressed);

        // Next day (e.g. 65,000 ticks later): eligible again
        var eligibleAgain = PawnMemoryTracker.SelectMilestone(list, 2, tick + 65000, cooldownTicks: 60000);
        Assert.NotNull(eligibleAgain);
    }

    [Fact]
    public void Milestone_SelectMilestone_RotatesRoundRobinAcrossDays()
    {
        var list = new List<MemoryEntry>();
        int tick = 100000;
        var m1 = new MemoryEntry(2, "Bob", "BecameLover", 65f, tick, "became lovers", false, false, true);
        var m2 = new MemoryEntry(2, "Bob", "Marriage", 80f, tick + 1000, "married in ceremony", false, false, true);
        list.Add(m1);
        list.Add(m2);

        // Day 1: m1 is selected first (earliest created among unrecalled)
        var day1 = PawnMemoryTracker.SelectMilestone(list, 2, tick, cooldownTicks: 60000);
        Assert.NotNull(day1);
        Assert.Equal("BecameLover", day1.EventKey);
        PawnMemoryTracker.MarkRecalled(day1, tick);

        // Same day: suppressed
        Assert.Null(PawnMemoryTracker.SelectMilestone(list, 2, tick + 5000, cooldownTicks: 60000));

        // Day 2 (65,000 ticks later): rotates to m2 (next unrecalled)
        int tickDay2 = tick + 65000;
        var day2 = PawnMemoryTracker.SelectMilestone(list, 2, tickDay2, cooldownTicks: 60000);
        Assert.NotNull(day2);
        Assert.Equal("Marriage", day2.EventKey);
        PawnMemoryTracker.MarkRecalled(day2, tickDay2);

        // Day 3 (130,000 ticks later): all recalled, rotates back to m1 (recalled at tick vs tickDay2)
        int tickDay3 = tick + 130000;
        var day3 = PawnMemoryTracker.SelectMilestone(list, 2, tickDay3, cooldownTicks: 60000);
        Assert.NotNull(day3);
        Assert.Equal("BecameLover", day3.EventKey);
    }

    [Fact]
    public void ColonySeniority_CalculatesSharedYearsAndFormatsByOpinion()
    {
        // 1 RimWorld year = 3,600,000 ticks
        float ticks3Years = 3 * 3600000f;
        float ticks5Years = 5 * 3600000f;

        int sharedYears = PawnMemoryTracker.CalculateSharedYears(ticks3Years, ticks5Years);
        Assert.Equal(3, sharedYears);

        // Positive opinion: Bond
        string bondStr = MemoryFormatter.FormatSeniority(sharedYears, 50);
        Assert.Equal("Bond: 3 years together in colony", bondStr);

        // Negative opinion: History of bitter coexistence
        string rivalStr = MemoryFormatter.FormatSeniority(sharedYears, -30);
        Assert.Equal("History: 3 years of bitter coexistence in colony", rivalStr);

        // Under 1 year (0 years)
        string zeroYearsStr = MemoryFormatter.FormatSeniority(0, 50);
        Assert.Equal(string.Empty, zeroYearsStr);
    }

    [Fact]
    public void MemoryFormatter_FormatMilestone_ContainsAntiRecitationInstruction()
    {
        var milestone = new MemoryEntry(2, "Bob", "SavedLife", 90f, 1000, "saved their life when near death", false, false, true);
        string formatted = MemoryFormatter.FormatMilestone(milestone, "Bob");

        Assert.Contains("Milestone: saved their life when near death", formatted);
        Assert.Contains("do not recite or quote this past event out of context", formatted);
    }

    [Fact]
    public void PawnMemoryTracker_SelectPersonalMemories_SelectsOnlyPersonalDeeds()
    {
        var list = new List<MemoryEntry>();
        int currentTick = 100000;

        // 1. Personal deed (TargetPawnId == -1)
        PawnMemoryTracker.AddOrUpdateMemory(list, -1, string.Empty, "CraftedArt", 35f, "crafted masterwork sculpture", currentTick);

        // 2. Personal crisis (TargetPawnId == -1)
        PawnMemoryTracker.AddOrUpdateMemory(list, -1, string.Empty, "MentalStateBerserk", -30f, "went into a berserk rage", currentTick);

        // 3. Relational memory with Bob (TargetPawnId == 2)
        PawnMemoryTracker.AddOrUpdateMemory(list, 2, "Bob", "RescuedMe", 30f, "rescued by Bob", currentTick);

        // 4. Core Trauma (TargetPawnId == -1, isCoreTrauma == true)
        PawnMemoryTracker.AddOrUpdateMemory(list, -1, string.Empty, "Trauma", -80f, "grief over spouse", currentTick, isCoreTrauma: true);

        // 5. Directive (TargetPawnId == -999, isDirective == true)
        PawnMemoryTracker.AddOrUpdateMemory(list, -999, "Player", "Directive", 50f, "guard the front gate", currentTick, isDirective: true);

        var personal = PawnMemoryTracker.SelectPersonalMemories(list, currentTick, maxMemories: 2);
        Assert.Equal(2, personal.Count);
        Assert.Contains(personal, m => m.EventKey == "CraftedArt");
        Assert.Contains(personal, m => m.EventKey == "MentalStateBerserk");
        Assert.DoesNotContain(personal, m => m.IsCoreTrauma);
        Assert.DoesNotContain(personal, m => m.IsDirective);
        Assert.DoesNotContain(personal, m => m.TargetPawnId >= 0);

        // Format personal memories
        string formatted = MemoryFormatter.FormatPersonalMemories(personal);
        Assert.StartsWith("Recent Experience: ", formatted);
        Assert.Contains("crafted masterwork sculpture", formatted);
        Assert.Contains("went into a berserk rage", formatted);

        // Relational recall must not leak personal memories
        var bobMemories = PawnMemoryTracker.SelectTopRecallMemories(list, 2, currentTick);
        Assert.Single(bobMemories);
        Assert.Equal("RescuedMe", bobMemories[0].EventKey);
    }

    [Fact]
    public void PawnMemoryTracker_DualTrackInterpersonalMemories_SelectsProminentAndDistinctRecent()
    {
        var list = new List<MemoryEntry>();
        int currentTick = 600000; // Day 10

        // 1. Historical prominent event 10 days ago (tick 1000)
        PawnMemoryTracker.AddOrUpdateMemory(list, 1, "Bob", "SavedLife", 80f, "saved their life when near death", 1000);

        // 2. Historical prominent conflict 5 days ago (tick 300000)
        PawnMemoryTracker.AddOrUpdateMemory(list, 1, "Bob", "SocialFight", -50f, "got into a violent fistfight", 300000);

        // 3. Recent grievance 2 hours ago (tick 595000)
        PawnMemoryTracker.AddOrUpdateMemory(list, 1, "Bob", "Insulted", -20f, "insulted by Bob", 595000);

        // 4. Recent medical care 1 hour ago (tick 597500)
        PawnMemoryTracker.AddOrUpdateMemory(list, 1, "Bob", "TendedMe", 25f, "treated their wounds", 597500);

        // 1. Prominent memories (Top 2 by absolute score)
        var prominent = PawnMemoryTracker.SelectTopRecallMemories(list, 1, currentTick, maxMemories: 2);
        Assert.Equal(2, prominent.Count);
        Assert.Contains(prominent, m => m.EventKey == "SocialFight");
        Assert.Contains(prominent, m => m.EventKey == "SavedLife" || m.EventKey == "TendedMe");

        // 2. Recent memories (Top 2 by CreatedTick, excluding prominent memories)
        var recent = PawnMemoryTracker.SelectRecentMemories(list, 1, currentTick, excludeMemories: prominent, maxMemories: 2);
        Assert.True(recent.Count <= 2);
        Assert.DoesNotContain(recent, m => prominent.Contains(m));

        // Format both lines
        string prominentLine = MemoryFormatter.FormatMemories("Bob", prominent);
        string recentLine = MemoryFormatter.FormatRecentMemories("Bob", recent);

        Assert.StartsWith("Memory with Bob: ", prominentLine);
        Assert.StartsWith("Recent with Bob: ", recentLine);

        // 3. Deduplication check: When total memories are <= 2, recent should be empty
        var smallList = new List<MemoryEntry>
        {
            new(1, "Bob", "RescuedMe", 30f, currentTick, "rescued by Bob")
        };
        var smallProminent = PawnMemoryTracker.SelectTopRecallMemories(smallList, 1, currentTick, maxMemories: 2);
        var smallRecent = PawnMemoryTracker.SelectRecentMemories(smallList, 1, currentTick, excludeMemories: smallProminent, maxMemories: 2);

        Assert.Single(smallProminent);
        Assert.Empty(smallRecent);
        Assert.Equal(string.Empty, MemoryFormatter.FormatRecentMemories("Bob", smallRecent));
    }

    [Fact]
    public void PawnMemoryTracker_Perspective_DifferentiatesActorAndTarget()
    {
        var list = new List<MemoryEntry>();
        int tick1 = 10000;
        int tick2 = 20000;

        // Actor records action toward Bob (id 1)
        PawnMemoryTracker.AddOrUpdateMemory("Alice", 2, list, 1, "Bob", "Arrest", -30f, "I arrested Bob", tick1, false, false, false, MemoryPerspective.Actor);

        // Same event key but Target perspective (e.g. mutual or separate projection)
        PawnMemoryTracker.AddOrUpdateMemory("Alice", 2, list, 1, "Bob", "Arrest", -40f, "Arrested by Bob", tick1 + 1000, false, false, false, MemoryPerspective.Target);

        // Should maintain 2 distinct entries because perspectives differ
        Assert.Equal(2, list.Count);
        var actorEntry = list.Find(m => m.Perspective == MemoryPerspective.Actor);
        var targetEntry = list.Find(m => m.Perspective == MemoryPerspective.Target);

        Assert.NotNull(actorEntry);
        Assert.NotNull(targetEntry);
        Assert.Equal(1, actorEntry.Count);
        Assert.Equal(1, targetEntry.Count);

        // Repeat actor action: should update actorEntry and increment Count
        PawnMemoryTracker.AddOrUpdateMemory("Alice", 2, list, 1, "Bob", "Arrest", -30f, "I arrested Bob again", tick2, false, false, false, MemoryPerspective.Actor);

        Assert.Equal(2, list.Count); // Count of memory items remains 2
        Assert.Equal(2, actorEntry.Count);
        Assert.Equal(tick2, actorEntry.LastTick);

        // Format to verify repeat indicator (x2)
        string formatted = MemoryFormatter.FormatMemories("Bob", new List<MemoryEntry> { actorEntry });
        Assert.Contains("I arrested Bob again (x2)", formatted);
    }
}
