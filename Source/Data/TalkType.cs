namespace RimTalk.Source.Data;

public enum TalkType
{
    Urgent,
    Hediff,
    LevelUp,
    Chitchat,
    Interaction,
    Event,
    QuestOffer,
    QuestEnd,
    Thought,
    User,
    Announcement,
    Sleep,
    Other
}

public static class TalkTypeExtensions
{
    public static bool IsFromUser(this TalkType talkType)
    {
        return talkType is TalkType.User or TalkType.Announcement;
    }

    public static bool IsFastTrack(this TalkType talkType)
    {
        return talkType is TalkType.User or TalkType.Announcement or TalkType.Interaction or TalkType.Urgent;
    }
}