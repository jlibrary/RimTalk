# RimTalk Architecture Analysis

## 概述 (Overview)

RimTalk 是一个 RimWorld mod，通过 LLM API 为游戏中的角色（pawns）生成动态对话。本文档分析了其架构，以帮助您保留 LLM 调用和 UI 部分，同时替换中间的游戏状态处理逻辑。

## 核心数据流 (Core Data Flow)

```
游戏事件 (Game Events)
    ↓
[Harmony Patches] → 触发对话请求
    ↓
[TalkService] → 决策层：何时、如何生成对话
    ↓
[PromptService] → 提取游戏信息 + 构建提示词 ★ 保留
    ↓
[AIService] → LLM API 调用 ★ 保留
    ↓
[TalkResponse] → 解析响应
    ↓
[PawnState.TalkResponses] → 排队等待显示
    ↓
[UI/BubblePatch] → 在游戏中显示对话气泡
```

## 第一个端点：游戏信息提取 (Game Information Extraction)

### 核心类：`PromptService` (Source/Service/PromptService.cs)

**关键方法：**

1. **`BuildContext(List<Pawn> pawns)`** - 主入口，构建多个 pawn 的完整上下文
   - 为每个 pawn 调用 `CreatePawnContext()`
   - 添加系统指令 (`Constant.Instruction`)
   - 格式化为 `[Person N START]...[Person N END]` 结构

2. **`CreatePawnContext(Pawn pawn, InfoLevel infoLevel)`** - 提取单个 pawn 的完整信息
   - 调用 `CreatePawnBackstory()` 获取背景
   - 添加健康状态 (Hediffs)
   - 添加个性 (Personality from Cache)
   - 添加心情 (Mood)
   - 添加记忆/想法 (Thoughts via `GetThoughts()`)
   - 添加关系 (Relations via `RelationsService`)
   - 添加装备 (Equipment)

3. **`CreatePawnBackstory(Pawn pawn, InfoLevel infoLevel)`** - 提取 pawn 背景信息
   - 姓名、头衔、性别、年龄
   - 角色 (Role: Colonist/Prisoner/Slave/Visitor/Enemy)
   - 种族 (Xenotype, if Biotech active)
   - 基因 (Genes)
   - 意识形态 (Ideology, Memes)
   - 童年/成年背景故事 (Childhood/Adulthood)
   - 特质 (Traits)
   - 技能 (Skills)

4. **`GetThoughts(Pawn pawn)`** - 提取 pawn 的当前想法
   - 从 `pawn.needs.mood.thoughts` 获取所有想法
   - 按 defName 分组并计算总 MoodOffset

**辅助类：`PawnService` (Source/Service/PawnService.cs)**

关键方法：
- `GetPawnStatusFull()` - 获取 pawn 完整状态（活动、附近的人、危险信息）
- `GetRole()` - 确定 pawn 角色（Colonist/Prisoner/Slave/Visitor/Enemy）
- `IsInDanger()` - 评估 pawn 是否处于危险中
- `IsInCombat()` - 检查 pawn 是否在战斗中
- `GetActivity()` - 获取当前活动描述
- `GetHostilePawnNearBy()` - 查找附近的敌对 pawn

**关系服务：`RelationsService` (Source/Service/RelationService.cs)**

提取 pawn 的社交关系信息。

## 第二个端点：最终提示词生成 (Final Prompt Generation)

### 核心方法：`PromptService.DecoratePrompt(TalkRequest, List<Pawn>, string status)`

**功能：**
将游戏上下文与对话提示结合，生成最终发送给 LLM 的完整提示词。

**添加的信息：**
1. **对话部分** - 根据 `TalkType` 格式化：
   - `TalkType.User`: 用户自定义对话
   - 其他类型：自动生成的对话（独白或多人对话）
   - 特殊情况：战斗、精神崩溃、倒下等紧急情况

2. **Pawn 状态** (status 参数)
   - 当前活动
   - 附近的人
   - 威胁信息

3. **环境上下文** (via `CommonUtil.GetInGameData()`):
   - 位置 (Location: Indoor/Outdoor)
   - 时间 (Time: 12-hour format)
   - 日期 (Date)
   - 季节 (Season)
   - 天气 (Weather)

4. **语言保证**
   - 如果是第一次指令，添加 `"in {Constant.Lang}"` 确保使用正确语言

**输出：** 更新 `talkRequest.Prompt` 为完整的提示词

## LLM 调用与 UI (要保留的部分)

### AIService (Source/Service/AIService.cs)

**核心方法：**

1. **`ChatStreaming<T>(...)`** - 流式聊天
   - 调用 `AIClientFactory.GetAIClient()`
   - 调用 `client.GetStreamingChatCompletionAsync<TalkResponse>()`
   - 对每个解析的角色对话执行回调
   - 更新统计信息和历史记录

2. **`Chat(...)`** - 非流式聊天
   - 同步调用，等待完整响应
   - 返回 `List<TalkResponse>`

3. **`Query<T>(...)`** - 一次性查询
   - 用于生成 persona 等

4. **`UpdateContext(string context)`** - 更新当前上下文
   - 存储从 `PromptService.BuildContext()` 生成的上下文

**重要状态：**
- `_instruction`: 当前对话上下文（系统提示词）
- `_busy`: 是否正在处理请求
- `_firstInstruction`: 是否是首次指令（用于添加语言说明）

### AIClientFactory (Source/Client/AIClientFactory.cs)

**功能：** 根据配置创建对应的 AI 客户端

**支持的提供商：**
- Google Gemini
- OpenAI
- DeepSeek
- OpenRouter
- Local models
- Custom endpoints

### Client 实现

**接口：`IAIClient` (Source/Client/IAIClient.cs)**

方法：
- `GetChatCompletionAsync()` - 非流式完成
- `GetStreamingChatCompletionAsync<T>()` - 流式完成，支持实时解析

**实现：**
- `OpenAIClient` - OpenAI 兼容 API
- `GeminiClient` - Google Gemini API

### UI 组件

1. **`CustomDialogueWindow` (Source/UI/CustomDialogueWindow.cs)**
   - 用户输入对话的窗口
   - 输入文本框和发送/取消按钮
   - 支持回车键发送

2. **`CustomDialogueService` (Source/Service/CustomDialogueService.cs)**
   - 管理待处理的自定义对话
   - 检查 pawn 是否可以交谈（距离、房间）
   - 执行对话（添加到 TalkRequest 队列）

3. **`Settings` UI (Source/Settings/)**
   - API 配置（API key, model, provider）
   - 基本设置（对话间隔、启用/禁用功能）
   - AI 指令自定义
   - 事件过滤器

## 中间处理层 (需要替换的部分)

### TalkService (Source/Service/TalkService.cs)

**核心职责：**
- 决定何时生成对话
- 选择对话参与者
- 编排异步 AI 请求
- 管理响应队列

**主要方法：**

1. **`GenerateTalk(TalkRequest talkRequest)`**
   - 检查是否应该生成对话（设置、AI 忙碌状态等）
   - 验证参与者资格
   - 检测危险情况并调整 TalkType
   - 选择附近的 pawns 参与对话
   - 构建上下文并装饰提示词
   - 启动异步生成任务

2. **`GenerateAndProcessTalkAsync(...)`**
   - 调用 `AIService.ChatStreaming()`
   - 处理流式响应
   - 将响应添加到 PawnState 队列
   - 更新对话历史

3. **`DisplayTalk()`**
   - 每个游戏 tick 调用
   - 从队列中取出并显示对话
   - 强制执行对话间隔
   - 创建游戏内交互日志条目

4. **`GetTalk(Pawn pawn)`**
   - 游戏 UI 调用以获取要显示的对话文本
   - 从队列中消费对话并标记为已说出

### PawnSelector (Source/Service/PawnSelector.cs)

**职责：** 为对话选择相关的 pawns

**方法：**
- `GetAllNearByPawns()` - 获取附近所有符合条件的 pawns

### 状态管理

**`PawnState` (Source/Data/PawnState.cs)**

Per-pawn 状态：
- `Context`: 从 `PromptService` 生成的上下文
- `TalkResponses`: 待显示的对话队列
- `TalkRequests`: 待生成的对话请求队列
- `LastTalkTick`: 上次说话时间
- `LastStatus`: 上次状态（用于避免重复）
- `IsGeneratingTalk`: 是否正在生成对话
- `Personality`: 个性（从 `PersonaService` 获取）

**`Cache` (Source/Data/Cache.cs)**

全局 pawn 状态缓存，`Dictionary<Pawn, PawnState>`

**`TalkHistory` (Source/Data/TalkHistory.cs)**

对话历史记录管理：
- `AddMessageHistory()` - 添加消息历史
- `GetMessageHistory()` - 获取对话历史（用于 LLM 上下文）
- `AddSpoken()` / `AddIgnored()` - 标记对话状态

**`ApiHistory` (Source/Data/ApiHistory.cs)**

API 调用历史记录（用于调试和统计）

### Harmony Patches (Source/Patch/)

拦截游戏事件以触发对话：

- **`ThoughtPatch`** - 当 pawn 获得新想法时
- **`MentalStatePatch`** - 当 pawn 进入精神状态时
- **`SkillLearnPatch`** - 当技能提升时
- **`BattleLogPatch`** - 战斗事件
- **`TickManagerPatch`** - 每个游戏 tick，调用 `TalkService.DisplayTalk()`
- **`BubblePatch`** - 显示对话气泡
- 等等...

## 数据契约 (Data Contracts)

这些是层之间的接口，应该保持不变：

### TalkRequest (Source/Data/TalkRequest.cs)

```csharp
public class TalkRequest
{
    public TalkType TalkType { get; set; }
    public string Prompt { get; set; }
    public Pawn Initiator { get; set; }
    public Pawn Recipient { get; set; }
    public int MapId { get; set; }
    public int CreatedTick { get; set; }
    public bool IsMonologue;
}
```

### TalkResponse (Source/Data/Json/TalkResponse.cs)

```csharp
[DataContract]
public class TalkResponse
{
    public Guid Id { get; set; }
    public TalkType TalkType { get; set; }
    [DataMember(Name = "name")] public string Name { get; set; }
    [DataMember(Name = "text")] public string Text { get; set; }
    public Guid ParentTalkId { get; set; }
}
```

### TalkType (Source/Data/TalkType.cs)

```csharp
public enum TalkType
{
    Other,    // 一般对话
    Thought,  // 想法触发的对话
    Event,    // 事件触发的对话
    User,     // 用户自定义对话
    Urgent,   // 紧急对话（战斗、危险等）
    QuestOffer // 任务提供
}
```

## 集成策略建议

### 方案 1: 接口抽象层

创建接口来分离关注点：

```csharp
// 保持不变的接口
public interface IGameInfoExtractor
{
    string BuildContext(List<Pawn> pawns);
    string CreatePawnContext(Pawn pawn, InfoLevel infoLevel);
    void DecoratePrompt(TalkRequest talkRequest, List<Pawn> pawns, string status);
}

public interface IAIServiceClient
{
    Task ChatStreaming<T>(TalkRequest request, List<(Role, string)> messages, 
        Dictionary<string, T> players, Action<T, TalkResponse> onPlayerResponseReceived);
    Task<List<TalkResponse>> Chat(TalkRequest request, List<(Role, string)> messages);
    void UpdateContext(string context);
}

// 您需要实现的新接口
public interface IConversationOrchestrator
{
    bool GenerateTalk(TalkRequest talkRequest);
    void DisplayTalk();
    string GetTalk(Pawn pawn);
}

public interface IPawnSelector
{
    List<Pawn> GetConversationParticipants(Pawn initiator, Pawn recipient);
}

public interface IStateManager
{
    PawnState GetState(Pawn pawn);
    void UpdateState(Pawn pawn, PawnState state);
}
```

**实现步骤：**

1. 将 `PromptService` 方法包装到 `IGameInfoExtractor` 实现中
2. 将 `AIService` 方法包装到 `IAIServiceClient` 实现中
3. 实现您自己的 `IConversationOrchestrator`、`IPawnSelector` 和 `IStateManager`
4. 更新 Harmony patches 调用您的新 `IConversationOrchestrator`

### 方案 2: Facade Pattern

创建一个 Facade 类来统一访问：

```csharp
public static class RimTalkFacade
{
    // 保留的原始功能
    public static class GameInfo
    {
        public static string ExtractPawnContext(Pawn pawn, InfoLevel level)
            => PromptService.CreatePawnContext(pawn, level);
        
        public static string BuildConversationContext(List<Pawn> pawns)
            => PromptService.BuildContext(pawns);
        
        public static void FinalizePrompt(TalkRequest request, List<Pawn> pawns, string status)
            => PromptService.DecoratePrompt(request, pawns, status);
    }
    
    public static class AI
    {
        public static Task<List<TalkResponse>> CallLLM(TalkRequest request, List<(Role, string)> messages)
            => AIService.Chat(request, messages);
        
        public static Task CallLLMStreaming<T>(TalkRequest request, ...)
            => AIService.ChatStreaming(request, ...);
    }
    
    // 您的新实现
    public static class ConversationManager
    {
        public static bool TryStartConversation(TalkRequest request)
        {
            // 您的新实现
        }
        
        public static void ProcessPendingConversations()
        {
            // 您的新实现
        }
    }
}
```

### 方案 3: 直接集成

直接在您的新代码中调用现有的方法：

```csharp
public class YourNewConversationSystem
{
    public async Task GenerateConversation(Pawn initiator, Pawn recipient)
    {
        // 1. 使用原始的游戏信息提取
        var pawns = new List<Pawn> { initiator, recipient };
        string context = PromptService.BuildContext(pawns);
        AIService.UpdateContext(context);
        
        // 2. 使用您的新逻辑创建请求
        var request = CreateOptimizedRequest(initiator, recipient);
        
        // 3. 使用原始的提示词装饰
        string status = PawnService.GetPawnStatusFull(initiator, nearbyPawns).Item1;
        PromptService.DecoratePrompt(request, pawns, status);
        
        // 4. 使用原始的 LLM 调用
        var responses = await AIService.Chat(request, GetMessageHistory(initiator));
        
        // 5. 使用您的新逻辑处理响应
        ProcessResponsesWithYourLogic(responses);
    }
}
```

## 关键观察和建议

### 优点（保持不变）

1. **游戏信息提取全面** - `PromptService` 和 `PawnService` 提取了丰富的游戏状态
2. **LLM 集成灵活** - 支持多个提供商和流式/非流式模式
3. **UI 成熟** - 自定义对话窗口和设置界面功能完善
4. **数据契约清晰** - `TalkRequest` 和 `TalkResponse` 定义明确

### 可改进区域（您的新实现）

1. **对话触发逻辑** - `TalkService.GenerateTalk()` 可以更智能地决定何时对话
2. **参与者选择** - `PawnSelector` 可以更好地选择相关 pawns
3. **状态管理** - `PawnState` 和 `Cache` 可以更高效
4. **队列管理** - `TalkRequestPool` 和响应队列可以优化优先级
5. **历史记录** - `TalkHistory` 可以更智能地管理上下文长度

### 推荐的起步步骤

1. **阶段 1: 理解**
   - 在本地构建并运行 mod
   - 启用调试日志，观察对话生成流程
   - 在 `TalkService.GenerateTalk()` 和 `AIService.ChatStreaming()` 设置断点

2. **阶段 2: 隔离**
   - 创建接口/facade 包装 `PromptService`、`PawnService`、`AIService`
   - 确保所有调用都通过您的接口

3. **阶段 3: 替换**
   - 实现您自己的对话编排逻辑
   - 使用原始的提取和 LLM 调用方法
   - 逐步替换状态管理、队列管理等

4. **阶段 4: 优化**
   - 基准测试性能改进
   - 优化您的实现
   - 添加新功能

### 测试建议

- 创建单元测试来验证您的新实现不会破坏 LLM 调用
- 测试各种场景：正常对话、紧急对话、用户对话、战斗对话
- 验证对话历史正确传递给 LLM
- 确保 UI 仍然正常工作

## 结论

RimTalk 的架构相对模块化，**游戏信息提取**（`PromptService` + `PawnService`）和 **LLM 调用**（`AIService` + Clients）是独立的端点，易于保留。中间的**对话编排和状态管理**（`TalkService`、`PawnState`、`Cache` 等）是您可以替换的部分，以实现更优的工程实现。

建议使用 Facade 或接口抽象层来清晰分离关注点，这样您可以逐步替换组件，同时保持系统的其余部分正常工作。
