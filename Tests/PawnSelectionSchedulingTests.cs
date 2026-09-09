using System;
using System.Collections.Generic;
using System.Linq;
using RimTalk.Source.Data;
using Xunit;

namespace RimTalk.Tests;

public class PawnSelectionSchedulingTests
{
    public class MockTalkRequest
    {
        public TalkType TalkType { get; set; }
        public int CreatedTick { get; set; }
    }

    public class MockPawnState
    {
        public string Name { get; set; }
        public bool CanGenerateTalk { get; set; } = true;
        public double TalkInitiationWeight { get; set; } = 1.0;
        public List<MockTalkRequest> TalkRequests { get; set; } = new();
    }

    /// <summary>
    /// Pure algorithmic implementation of SelectNextAvailablePawn for unit testing,
    /// matching the exact logic in PawnSelector.SelectNextAvailablePawn().
    /// </summary>
    private static MockPawnState SelectNextAvailablePawn(
        IEnumerable<MockPawnState> pawnStates,
        Func<List<MockPawnState>, MockPawnState> weightedSelector = null)
    {
        weightedSelector ??= candidates =>
        {
            if (candidates == null || candidates.Count == 0) return null;
            double totalWeight = candidates.Sum(p => Math.Max(0.0, p.TalkInitiationWeight));
            if (totalWeight <= 0.0) return null;
            double rand = new Random(42).NextDouble() * totalWeight;
            double current = 0.0;
            foreach (var p in candidates)
            {
                current += Math.Max(0.0, p.TalkInitiationWeight);
                if (current >= rand) return p;
            }
            return candidates.Last();
        };

        MockPawnState pawnWithOldestUserRequest = null;
        int oldestTick = int.MaxValue;
        var talkReadyPawns = new List<MockPawnState>();

        foreach (var pawnState in pawnStates)
        {
            if (pawnState == null) continue;

            bool canTalk = pawnState.CanGenerateTalk;
            if (canTalk && pawnState.TalkInitiationWeight > 0)
            {
                talkReadyPawns.Add(pawnState);
            }

            foreach (var req in pawnState.TalkRequests)
            {
                if (req.TalkType.IsFromUser() && req.CreatedTick < oldestTick)
                {
                    oldestTick = req.CreatedTick;
                    pawnWithOldestUserRequest = pawnState;
                }
            }
        }

        return pawnWithOldestUserRequest ?? 
               (talkReadyPawns.Count > 0 ? weightedSelector(talkReadyPawns) : null);
    }

    [Fact]
    public void UserRequest_HasAbsolutePriority_OverHigherWeightColonists()
    {
        var colonistA = new MockPawnState { Name = "ColonistA", TalkInitiationWeight = 10.0 };
        var colonistB = new MockPawnState
        {
            Name = "ColonistB",
            TalkInitiationWeight = 0.01,
            TalkRequests = { new MockTalkRequest { TalkType = TalkType.User, CreatedTick = 100 } }
        };

        var selected = SelectNextAvailablePawn(new[] { colonistA, colonistB });
        Assert.Same(colonistB, selected);
    }

    [Fact]
    public void UserRequest_FollowsFIFOCreatedTick_WhenMultipleRequestsExist()
    {
        var colonistA = new MockPawnState
        {
            Name = "ColonistA",
            TalkRequests = { new MockTalkRequest { TalkType = TalkType.User, CreatedTick = 500 } }
        };
        var colonistB = new MockPawnState
        {
            Name = "ColonistB",
            TalkRequests = { new MockTalkRequest { TalkType = TalkType.Announcement, CreatedTick = 200 } }
        };

        var selected = SelectNextAvailablePawn(new[] { colonistA, colonistB });
        Assert.Same(colonistB, selected); // 200 is older than 500 -> ColonistB wins
    }

    [Fact]
    public void TalkTypeOther_DoesNotMonopolize_OrBypassWeights()
    {
        // Regression scenario reported by user:
        // ColonistA is first in iteration order and repeatedly has TalkType.Other.
        // ColonistB and ColonistC have equal weight (0.7).
        var colonistA = new MockPawnState
        {
            Name = "ColonistA",
            TalkInitiationWeight = 0.7,
            TalkRequests = { new MockTalkRequest { TalkType = TalkType.Other, CreatedTick = 50 } }
        };
        var colonistB = new MockPawnState { Name = "ColonistB", TalkInitiationWeight = 0.7 };
        var colonistC = new MockPawnState { Name = "ColonistC", TalkInitiationWeight = 0.7 };

        var candidates = new[] { colonistA, colonistB, colonistC };

        // Test with real random distributions across 10,000 iterations
        var rng = new Random(12345);
        var counts = new Dictionary<string, int>
        {
            ["ColonistA"] = 0,
            ["ColonistB"] = 0,
            ["ColonistC"] = 0
        };

        for (int i = 0; i < 10000; i++)
        {
            var picked = SelectNextAvailablePawn(candidates, list =>
            {
                double total = list.Sum(p => p.TalkInitiationWeight);
                double roll = rng.NextDouble() * total;
                double acc = 0;
                foreach (var p in list)
                {
                    acc += p.TalkInitiationWeight;
                    if (acc >= roll) return p;
                }
                return list.Last();
            });

            Assert.NotNull(picked);
            counts[picked.Name]++;
        }

        // Each colonist with 0.7 weight should get approximately 33.3% of selections (~3333 times).
        // Under the previous buggy 1.2.0 logic, ColonistA got 100% (10,000) and ColonistB/C got 0%.
        Assert.InRange(counts["ColonistA"], 3000, 3600);
        Assert.InRange(counts["ColonistB"], 3000, 3600);
        Assert.InRange(counts["ColonistC"], 3000, 3600);
    }

    [Fact]
    public void TalkInitiationWeight_DistributesProportionally_ToConfiguredWeights()
    {
        var heavyTalker = new MockPawnState { Name = "Heavy", TalkInitiationWeight = 2.0 };
        var quietTalker = new MockPawnState { Name = "Quiet", TalkInitiationWeight = 1.0 };
        var silentTalker = new MockPawnState { Name = "Silent", TalkInitiationWeight = 0.0 };

        var candidates = new[] { heavyTalker, quietTalker, silentTalker };
        var rng = new Random(9876);
        var counts = new Dictionary<string, int> { ["Heavy"] = 0, ["Quiet"] = 0, ["Silent"] = 0 };

        for (int i = 0; i < 10000; i++)
        {
            var picked = SelectNextAvailablePawn(candidates, list =>
            {
                double total = list.Sum(p => p.TalkInitiationWeight);
                double roll = rng.NextDouble() * total;
                double acc = 0;
                foreach (var p in list)
                {
                    acc += p.TalkInitiationWeight;
                    if (acc >= roll) return p;
                }
                return list.Last();
            });

            Assert.NotNull(picked);
            counts[picked.Name]++;
        }

        // Silent talker must NEVER be selected
        Assert.Equal(0, counts["Silent"]);

        // Heavy should get ~66.7% (approx 6667) and Quiet should get ~33.3% (approx 3333)
        Assert.InRange(counts["Heavy"], 6300, 7000);
        Assert.InRange(counts["Quiet"], 3000, 3700);
    }

    [Fact]
    public void InactiveOrSleepingPawns_AreExcluded_EvenIfTheyHavePendingRequests()
    {
        var sleepingPawn = new MockPawnState
        {
            Name = "Sleeping",
            CanGenerateTalk = false,
            TalkRequests = { new MockTalkRequest { TalkType = TalkType.Other, CreatedTick = 10 } }
        };
        var activePawn = new MockPawnState
        {
            Name = "Active",
            CanGenerateTalk = true,
            TalkInitiationWeight = 1.0
        };

        var selected = SelectNextAvailablePawn(new[] { sleepingPawn, activePawn });
        Assert.Same(activePawn, selected);
    }

    [Fact]
    public void EmptyCandidates_OrAllInactive_ReturnsNull()
    {
        Assert.Null(SelectNextAvailablePawn(Array.Empty<MockPawnState>()));

        var allInactive = new[]
        {
            new MockPawnState { Name = "Busy1", CanGenerateTalk = false },
            new MockPawnState { Name = "Busy2", CanGenerateTalk = false }
        };
        Assert.Null(SelectNextAvailablePawn(allInactive));
    }
}
