namespace RimTalk.Source.Data;

// 发言类型
public enum TalkType
{
    Urgent,     // 紧急
    Hediff,     // 疾病相关
    LevelUp,    // 升级
    Chitchat,   // 闲聊
    Event,      // 事件
    QuestOffer, // 任务提供
    QuestEnd,   // 任务结束
    Thought,    // 想法
    User,       // 用户触发
    Other       // 其他
}