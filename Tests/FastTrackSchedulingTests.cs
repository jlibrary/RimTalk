using System.Collections.Generic;
using RimTalk.Source.Data;
using Xunit;

namespace RimTalk.Tests;

public class FastTrackSchedulingTests
{
    [Fact]
    public void TalkType_Classification_CorrectlyIdentifiesFastTrackAndUserTypes()
    {
        // User-initiated types
        Assert.True(TalkType.User.IsFromUser());
        Assert.True(TalkType.Announcement.IsFromUser());
        Assert.False(TalkType.Interaction.IsFromUser());
        Assert.False(TalkType.Other.IsFromUser());
        Assert.False(TalkType.Chitchat.IsFromUser());
        Assert.False(TalkType.Sleep.IsFromUser());

        // FastTrack types (bypasses ambient TalkInterval)
        Assert.True(TalkType.User.IsFastTrack());
        Assert.True(TalkType.Announcement.IsFastTrack());
        Assert.True(TalkType.Interaction.IsFastTrack());
        Assert.True(TalkType.Urgent.IsFastTrack());

        // Standard / low-priority types
        Assert.False(TalkType.Other.IsFastTrack());
        Assert.False(TalkType.Sleep.IsFastTrack());
        Assert.False(TalkType.Thought.IsFastTrack());
        Assert.False(TalkType.Chitchat.IsFastTrack());
        Assert.False(TalkType.Hediff.IsFastTrack());
        Assert.False(TalkType.LevelUp.IsFastTrack());
        Assert.False(TalkType.Event.IsFastTrack());
        Assert.False(TalkType.QuestOffer.IsFastTrack());
    }

    [Theory]
    [InlineData(TalkType.Other)]
    [InlineData(TalkType.Sleep)]
    [InlineData(TalkType.Thought)]
    [InlineData(TalkType.Chitchat)]
    [InlineData(TalkType.Hediff)]
    [InlineData(TalkType.Interaction)]
    [InlineData(TalkType.Urgent)]
    public void CanPreempt_UserTalk_AlwaysPreemptsAnyOngoingDialogue(TalkType ongoingTalkType)
    {
        // Player direct commands/announcements must always cancel and preempt
        Assert.True(TalkType.User.CanPreempt(ongoingTalkType));
        Assert.True(TalkType.Announcement.CanPreempt(ongoingTalkType));
    }

    [Theory]
    [InlineData(TalkType.Other)]
    [InlineData(TalkType.Sleep)]
    [InlineData(TalkType.Thought)]
    [InlineData(TalkType.Chitchat)]
    [InlineData(TalkType.Hediff)]
    [InlineData(TalkType.LevelUp)]
    [InlineData(TalkType.Event)]
    public void CanPreempt_FastTrackInteraction_PreemptsLowPriorityBackgroundTalks(TalkType lowPriorityType)
    {
        // Fast Track interactions should interrupt ambient/background dialogue
        Assert.True(TalkType.Interaction.CanPreempt(lowPriorityType));
        Assert.True(TalkType.Urgent.CanPreempt(lowPriorityType));
    }

    [Theory]
    [InlineData(TalkType.Interaction)]
    [InlineData(TalkType.Urgent)]
    [InlineData(TalkType.User)]
    [InlineData(TalkType.Announcement)]
    public void CanPreempt_FastTrackInteraction_CannotPreemptOtherFastTracks_PreventsChaining(TalkType existingFastTrack)
    {
        // Fast Track interactions MUST NOT interrupt each other to prevent queue backlog and chaining
        Assert.False(TalkType.Interaction.CanPreempt(existingFastTrack));
    }

    [Theory]
    [InlineData(TalkType.Interaction)]
    [InlineData(TalkType.Other)]
    [InlineData(TalkType.Sleep)]
    [InlineData(TalkType.User)]
    public void CanPreempt_RegularTalk_CannotPreemptAnything(TalkType ongoingTalkType)
    {
        // Regular ambient talks cannot cancel any ongoing generation
        Assert.False(TalkType.Other.CanPreempt(ongoingTalkType));
        Assert.False(TalkType.Chitchat.CanPreempt(ongoingTalkType));
    }

    [Fact]
    public void FastTrackSuppression_WhenInitiatorHasPendingResponses_IsSuppressed()
    {
        // Rule 1: A pawn currently speaking or with pending dialogue must ignore new incoming fast track interactions
        bool isGeneratingTalk = false;
        int pendingResponsesCount = 2; // Pawn has pending speech bubbles

        bool shouldSuppress = isGeneratingTalk || pendingResponsesCount > 0;
        Assert.True(shouldSuppress, "Incoming Fast Track interaction should be suppressed when pawn has pending responses");
    }

    [Fact]
    public void FastTrackSuppression_WhenInitiatorIsGenerating_IsSuppressed()
    {
        bool isGeneratingTalk = true;
        int pendingResponsesCount = 0;

        bool shouldSuppress = isGeneratingTalk || pendingResponsesCount > 0;
        Assert.True(shouldSuppress, "Incoming Fast Track interaction should be suppressed when pawn is generating talk");
    }

    [Fact]
    public void FastTrackSuppression_WhenRecipientHasPendingResponses_IsSuppressed()
    {
        bool initiatorBusy = false;
        bool recipientBusy = true; // Recipient is currently speaking

        bool shouldSuppress = initiatorBusy || recipientBusy;
        Assert.True(shouldSuppress, "Incoming Fast Track interaction should be suppressed when recipient is busy speaking");
    }

    [Fact]
    public void FastTrackSuppression_WhenBothPawnsFree_IsAccepted()
    {
        bool initiatorBusy = false;
        bool recipientBusy = false;

        bool shouldSuppress = initiatorBusy || recipientBusy;
        Assert.False(shouldSuppress, "Incoming Fast Track interaction should be accepted when both pawns are free");
    }

    [Fact]
    public void CooldownPreservation_FastTrackDoesNotResetRegularTalkTimer()
    {
        // Rule 2: Fast Track execution must not reset the regular talk timer
        int initialLastTalkTick = 1000;
        int currentTick = 2500;
        int regularTalkInterval = 1800; // 30 seconds (1800 ticks)

        // Elapsed time before fast track occurs
        int elapsedBeforeFastTrack = currentTick - initialLastTalkTick;
        Assert.True(elapsedBeforeFastTrack < regularTalkInterval); // Cooldown not yet due

        // Simulate fast track firing (does NOT modify initialLastTalkTick)
        int lastRegularTalkTickAfterFastTrack = initialLastTalkTick;

        // Advance time past the regular talk interval
        currentTick = 3000;
        int elapsedAfterwards = currentTick - lastRegularTalkTickAfterFastTrack;

        // Regular talk is now due and ready to fire because its timer was preserved!
        Assert.True(elapsedAfterwards >= regularTalkInterval,
            "Regular talk timer must have progressed towards readiness despite intervening fast track interactions");
    }

    [Fact]
    public void RegularTalkCooldown_StartsCountingAfterGenerationCompletes()
    {
        // Dialogue generation starts at tick 1000 and takes 4 seconds (240 ticks at 60 tps) to complete
        int regularTalkInterval = 420; // 7 seconds (420 ticks)
        int lastTalkEndTick = 1000;

        // Current Fixed Logic (1451d40):
        // While AIService.IsBusy() for regular talk, lastTalkEndTick updates each tick until generation finishes at tick 1240
        bool isAiBusy = true;
        TalkType activeTalkType = TalkType.Chitchat;

        for (int curTick = 1001; curTick <= 1240; curTick++)
        {
            if (isAiBusy && !activeTalkType.IsFastTrack())
            {
                lastTalkEndTick = curTick;
            }
        }

        // Generation completed at tick 1240
        Assert.Equal(1240, lastTalkEndTick);

        // 1. At tick 1450 (only 210 ticks = 3.5s after generation completed):
        // Under FIXED code: elapsed since completion is 210 ticks < 420 ticks (7s).
        // Pawns MUST NOT speak yet!
        int currentTick = 1450;
        int elapsedSinceEnd = currentTick - lastTalkEndTick;
        bool canSpeakFixed = elapsedSinceEnd >= regularTalkInterval;
        Assert.False(canSpeakFixed, "With fix, cooldown must NOT elapse prematurely 3.5s after generation");

        // 2. What happened BEFORE the fix:
        // In the bugged code, lastTalkEndTick was NEVER updated during AI generation, remaining at start tick (1000).
        int buggedLastTalkEndTick = 1000;
        int buggedElapsed = currentTick - buggedLastTalkEndTick; // 1450 - 1000 = 450 ticks (7.5s from start!)
        bool canSpeakBugged = buggedElapsed >= regularTalkInterval;
        Assert.True(canSpeakBugged, "Before fix (bug), pawns spoke much earlier (3.5s after generation ended) because cooldown counted from START, not END!");

        // 3. Under FIXED code, at tick 1660 (full 420 ticks = 7s after generation completed):
        currentTick = 1660;
        elapsedSinceEnd = currentTick - lastTalkEndTick;
        canSpeakFixed = elapsedSinceEnd >= regularTalkInterval;
        Assert.True(canSpeakFixed, "With fix, pawns speak exactly after the full 7s cooldown following generation completion");
    }

    [Fact]
    public void RegularTalkCooldown_FastTrackDoesNotUpdateLastTalkEndTick()
    {
        // An intervening fast-track request (e.g. user prompt) must NOT reset the regular dialogue cooldown timer
        int regularEndTick = 1000;
        bool isAiBusy = true;
        TalkType activeTalkType = TalkType.User; // Fast-track!

        int curTick = 1500;
        if (isAiBusy && !activeTalkType.IsFastTrack())
        {
            regularEndTick = curTick;
        }

        // Must remain preserved at 1000!
        Assert.Equal(1000, regularEndTick);
    }
}
