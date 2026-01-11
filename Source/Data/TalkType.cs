namespace RimTalk.Source.Data;

public enum TalkType
{
    Urgent,
    Hediff,
    LevelUp,
    Chitchat,
    Event,
    QuestOffer,
    QuestEnd,
    Thought,
    User,
    Other
}

public static class TalkTypeExtensions
{
    public static bool IsFromUser(this TalkType talkType)
    {
        return talkType is TalkType.User;
    }
}