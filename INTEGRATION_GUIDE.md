# RimTalk 集成指南 (Integration Guide)

## 快速开始 (Quick Start)

本指南提供具体的代码示例，展示如何保留 RimTalk 的 LLM 调用和游戏信息提取功能，同时替换中间的处理逻辑。

## 核心调用示例 (Core API Usage Examples)

### 1. 提取游戏信息 (Extracting Game Information)

#### 提取单个 Pawn 的上下文

```csharp
using RimTalk.Service;
using RimTalk.Data;
using Verse;

// 获取一个 pawn 的完整上下文信息
Pawn myPawn = ...; // 从游戏中获取的 pawn

// 短信息（用于非主要参与者）
string shortContext = PromptService.CreatePawnContext(myPawn, PromptService.InfoLevel.Short);

// 正常信息（默认，用于主要参与者）
string normalContext = PromptService.CreatePawnContext(myPawn, PromptService.InfoLevel.Normal);

// 完整信息（包含完整的背景故事描述）
string fullContext = PromptService.CreatePawnContext(myPawn, PromptService.InfoLevel.Full);
```

#### 提取多个 Pawns 的对话上下文

```csharp
using System.Collections.Generic;

List<Pawn> conversationParticipants = new List<Pawn>
{
    initiatorPawn,    // 第一个 pawn 获得 Normal 详细级别
    recipientPawn,    // 其他 pawns 获得 Short 详细级别
    nearbyPawn1
};

// 构建包含系统指令的完整上下文
string fullContext = PromptService.BuildContext(conversationParticipants);

// fullContext 包含：
// - Constant.Instruction (系统提示词)
// - [Person 1 START] ... [Person 1 END]
// - [Person 2 START] ... [Person 2 END]
// - [Person 3 START] ... [Person 3 END]
```

#### 获取 Pawn 状态信息

```csharp
// 获取 pawn 的完整当前状态
List<Pawn> nearbyPawns = PawnSelector.GetAllNearByPawns(myPawn);
var (statusString, isInDanger) = myPawn.GetPawnStatusFull(nearbyPawns);

// statusString 包含：
// - 当前活动 (e.g., "John (Colonist) (Researching)")
// - 附近的人及其状态
// - 威胁信息（如果有敌人）
// - 访客/入侵者状态
// - 系统提示

// isInDanger 指示是否应该使用紧急语气
```

#### 获取其他有用信息

```csharp
// 角色
string role = myPawn.GetRole(includeFaction: true); // "Colonist", "Prisoner", "Visitor Group(Outlanders)"

// 战斗状态
bool inCombat = myPawn.IsInCombat();

// 危险评估
bool inDanger = myPawn.IsInDanger(includeMentalState: true);

// 当前想法
Dictionary<Thought, float> thoughts = PromptService.GetThoughts(myPawn);
foreach (var (thought, moodImpact) in thoughts)
{
    // thought.LabelCap: 想法标签
    // moodImpact: 对心情的影响（正数或负数）
}

// 位置状态
string location = PromptService.GetPawnLocationStatus(myPawn); // "Indoors" or "Outdoors"
```

### 2. 生成最终提示词 (Generating Final Prompt)

```csharp
using RimTalk.Data;
using RimTalk.Source.Data;

// 创建对话请求
TalkRequest request = new TalkRequest(
    prompt: "discussing the recent battle",  // 初始提示
    initiator: myPawn,
    recipient: otherPawn,
    talkType: TalkType.Other
);

// 获取参与者和状态
List<Pawn> participants = new List<Pawn> { myPawn, otherPawn };
List<Pawn> nearbyPawns = PawnSelector.GetAllNearByPawns(myPawn);
var (status, isInDanger) = myPawn.GetPawnStatusFull(nearbyPawns);

// 装饰提示词（添加环境上下文、时间、天气等）
PromptService.DecoratePrompt(request, participants, status);

// 现在 request.Prompt 包含完整的提示词：
// - 对话格式说明（独白/多人对话/紧急）
// - Pawn 状态
// - 位置、时间、日期、季节、天气
// - 语言说明（如果是第一次）
```

### 3. 调用 LLM (Calling the LLM)

#### 非流式调用

```csharp
using System.Threading.Tasks;

// 准备消息历史
List<(Role role, string message)> messageHistory = TalkHistory.GetMessageHistory(myPawn);

// 更新 AI 上下文
string context = PromptService.BuildContext(participants);
AIService.UpdateContext(context);

// 调用 LLM
List<TalkResponse> responses = await AIService.Chat(request, messageHistory);

// 处理响应
foreach (var response in responses)
{
    Console.WriteLine($"{response.Name}: {response.Text}");
    // response.Id: 唯一标识符
    // response.TalkType: 对话类型
}
```

#### 流式调用（推荐用于实时显示）

```csharp
// 创建 pawn 名称到 Pawn 对象的字典
Dictionary<string, Pawn> playerDict = participants.ToDictionary(
    p => p.LabelShort,
    p => p
);

// 流式调用，每次解析到一个角色的完整对话时执行回调
await AIService.ChatStreaming(
    request,
    messageHistory,
    playerDict,
    onPlayerResponseReceived: (pawn, talkResponse) =>
    {
        // 实时处理每个角色的对话
        Console.WriteLine($"[STREAM] {pawn.LabelShort}: {talkResponse.Text}");
        
        // 您可以在这里：
        // - 立即显示对话
        // - 添加到队列
        // - 触发动画
        // - 更新 UI
    }
);
```

#### 一次性查询（用于生成 Persona 等）

```csharp
using RimTalk.Data;

TalkRequest personaRequest = new TalkRequest(
    prompt: Constant.PersonaGenInstruction, // 生成 persona 的指令
    initiator: myPawn
);

PersonalityData personality = await AIService.Query<PersonalityData>(personaRequest);

if (personality != null)
{
    Console.WriteLine($"Persona: {personality.Persona}");
    Console.WriteLine($"Chattiness: {personality.Chattiness}");
}
```

### 4. 用户自定义对话 (User-Initiated Dialogue)

```csharp
using RimTalk.Service;
using RimTalk.UI;

// 检查是否可以对话（距离和房间检查）
if (CustomDialogueService.CanTalk(initiator, recipient))
{
    // 立即执行对话
    CustomDialogueService.ExecuteDialogue(initiator, recipient, "Hello there!");
}
else
{
    // 或者打开对话窗口让用户输入
    Find.WindowStack.Add(new CustomDialogueWindow(initiator, recipient));
}
```

## 集成模式示例 (Integration Pattern Examples)

### 模式 1: Facade 包装器

创建一个统一的访问点来调用原始功能：

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RimTalk.Service;
using RimTalk.Data;
using Verse;

namespace YourMod
{
    /// <summary>
    /// Facade 类，提供对 RimTalk 核心功能的简化访问
    /// </summary>
    public static class RimTalkCore
    {
        #region Game Information Extraction
        
        /// <summary>
        /// 提取单个 pawn 的完整上下文信息
        /// </summary>
        public static string ExtractPawnInfo(Pawn pawn, bool detailed = false)
        {
            var level = detailed 
                ? PromptService.InfoLevel.Full 
                : PromptService.InfoLevel.Normal;
            return PromptService.CreatePawnContext(pawn, level);
        }
        
        /// <summary>
        /// 为对话构建多 pawn 上下文
        /// </summary>
        public static string BuildConversationContext(List<Pawn> pawns)
        {
            return PromptService.BuildContext(pawns);
        }
        
        /// <summary>
        /// 获取 pawn 的当前状态字符串
        /// </summary>
        public static (string status, bool isInDanger) GetPawnStatus(Pawn pawn)
        {
            List<Pawn> nearbyPawns = PawnSelector.GetAllNearByPawns(pawn);
            return pawn.GetPawnStatusFull(nearbyPawns);
        }
        
        #endregion
        
        #region Prompt Generation
        
        /// <summary>
        /// 创建并装饰完整的对话请求
        /// </summary>
        public static TalkRequest CreateDialogueRequest(
            Pawn initiator, 
            Pawn recipient, 
            string basePrompt,
            TalkType talkType = TalkType.Other)
        {
            var request = new TalkRequest(basePrompt, initiator, recipient, talkType);
            
            var participants = new List<Pawn> { initiator };
            if (recipient != null && recipient != initiator)
                participants.Add(recipient);
            
            var (status, _) = GetPawnStatus(initiator);
            PromptService.DecoratePrompt(request, participants, status);
            
            return request;
        }
        
        #endregion
        
        #region LLM Calls
        
        /// <summary>
        /// 调用 LLM 生成对话（非流式）
        /// </summary>
        public static async Task<List<TalkResponse>> GenerateDialogue(
            TalkRequest request,
            List<Pawn> participants)
        {
            // 更新上下文
            string context = BuildConversationContext(participants);
            AIService.UpdateContext(context);
            
            // 获取历史
            var history = TalkHistory.GetMessageHistory(request.Initiator);
            
            // 调用 LLM
            return await AIService.Chat(request, history);
        }
        
        /// <summary>
        /// 调用 LLM 生成对话（流式）
        /// </summary>
        public static async Task GenerateDialogueStreaming(
            TalkRequest request,
            List<Pawn> participants,
            Action<Pawn, TalkResponse> onResponseReceived)
        {
            // 更新上下文
            string context = BuildConversationContext(participants);
            AIService.UpdateContext(context);
            
            // 创建 pawn 字典
            var playerDict = participants.ToDictionary(p => p.LabelShort, p => p);
            
            // 获取历史
            var history = TalkHistory.GetMessageHistory(request.Initiator);
            
            // 调用流式 LLM
            await AIService.ChatStreaming(
                request,
                history,
                playerDict,
                onResponseReceived
            );
        }
        
        #endregion
        
        #region Utilities
        
        /// <summary>
        /// 检查 AI 是否正在处理请求
        /// </summary>
        public static bool IsAIBusy() => AIService.IsBusy();
        
        /// <summary>
        /// 获取 pawn 的对话历史
        /// </summary>
        public static List<(Role, string)> GetConversationHistory(Pawn pawn)
        {
            return TalkHistory.GetMessageHistory(pawn);
        }
        
        #endregion
    }
}
```

### 模式 2: 您自己的对话系统

使用 RimTalk 的核心功能实现您自己的对话系统：

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RimTalk.Service;
using RimTalk.Data;
using Verse;

namespace YourMod
{
    /// <summary>
    /// 您的优化对话系统
    /// </summary>
    public class OptimizedConversationSystem
    {
        // 您的新状态管理
        private readonly Dictionary<Pawn, YourPawnState> _pawnStates = new();
        
        // 您的新队列管理
        private readonly PriorityQueue<ConversationRequest> _requestQueue = new();
        
        /// <summary>
        /// 请求生成对话（您的新逻辑）
        /// </summary>
        public void RequestConversation(Pawn initiator, Pawn recipient, string topic)
        {
            // 您的新逻辑来决定是否应该生成对话
            if (!ShouldGenerateConversation(initiator, recipient))
                return;
            
            // 添加到您的优先级队列
            _requestQueue.Enqueue(new ConversationRequest
            {
                Initiator = initiator,
                Recipient = recipient,
                Topic = topic,
                Priority = CalculatePriority(initiator, recipient, topic)
            });
        }
        
        /// <summary>
        /// 处理队列中的对话请求（每个 tick 或定时调用）
        /// </summary>
        public async Task ProcessPendingConversations()
        {
            if (_requestQueue.IsEmpty || AIService.IsBusy())
                return;
            
            var request = _requestQueue.Dequeue();
            await GenerateConversationAsync(request);
        }
        
        /// <summary>
        /// 生成对话的核心方法
        /// </summary>
        private async Task GenerateConversationAsync(ConversationRequest request)
        {
            try
            {
                // 1. 使用 RimTalk 的参与者选择逻辑（或您自己的）
                List<Pawn> participants = SelectParticipants(request.Initiator, request.Recipient);
                
                // 2. 使用 RimTalk 的游戏信息提取
                string context = PromptService.BuildContext(participants);
                AIService.UpdateContext(context);
                
                // 3. 创建并装饰请求
                var talkRequest = new TalkRequest(
                    request.Topic,
                    request.Initiator,
                    request.Recipient,
                    DetermineTalkType(request)
                );
                
                var (status, _) = request.Initiator.GetPawnStatusFull(
                    PawnSelector.GetAllNearByPawns(request.Initiator)
                );
                
                PromptService.DecoratePrompt(talkRequest, participants, status);
                
                // 4. 使用 RimTalk 的 LLM 调用（流式）
                var playerDict = participants.ToDictionary(p => p.LabelShort, p => p);
                var history = TalkHistory.GetMessageHistory(request.Initiator);
                
                await AIService.ChatStreaming(
                    talkRequest,
                    history,
                    playerDict,
                    (pawn, response) => OnResponseReceived(pawn, response, request)
                );
            }
            catch (Exception ex)
            {
                Log.Error($"[YourMod] Failed to generate conversation: {ex}");
            }
        }
        
        /// <summary>
        /// 处理流式响应（您的新逻辑）
        /// </summary>
        private void OnResponseReceived(Pawn pawn, TalkResponse response, ConversationRequest request)
        {
            // 您的新逻辑来处理响应
            // 例如：
            // - 立即显示
            // - 添加到您的队列
            // - 触发事件
            // - 更新统计信息
            
            var state = GetOrCreateState(pawn);
            state.AddPendingDialogue(response);
            
            // 可选：更新历史记录
            TalkHistory.AddMessageHistory(pawn, request.Topic, response.Text);
        }
        
        // 您的新辅助方法
        private bool ShouldGenerateConversation(Pawn initiator, Pawn recipient)
        {
            // 您的新逻辑
            return true;
        }
        
        private List<Pawn> SelectParticipants(Pawn initiator, Pawn recipient)
        {
            // 您可以使用 RimTalk 的选择器
            var nearby = PawnSelector.GetAllNearByPawns(initiator);
            
            // 或实现您自己的选择逻辑
            var participants = new List<Pawn> { initiator };
            if (recipient != null) participants.Add(recipient);
            
            // 添加相关的附近 pawns
            participants.AddRange(nearby.Take(2));
            
            return participants.Distinct().ToList();
        }
        
        private TalkType DetermineTalkType(ConversationRequest request)
        {
            // 您的新逻辑来确定对话类型
            if (request.Initiator.IsInDanger())
                return TalkType.Urgent;
            
            return TalkType.Other;
        }
        
        private int CalculatePriority(Pawn initiator, Pawn recipient, string topic)
        {
            // 您的新优先级计算逻辑
            int priority = 0;
            
            if (initiator.IsInDanger()) priority += 100;
            if (initiator.InMentalState) priority += 50;
            // ... 更多逻辑
            
            return priority;
        }
        
        private YourPawnState GetOrCreateState(Pawn pawn)
        {
            if (!_pawnStates.ContainsKey(pawn))
                _pawnStates[pawn] = new YourPawnState(pawn);
            
            return _pawnStates[pawn];
        }
    }
    
    // 您的新数据结构
    public class ConversationRequest
    {
        public Pawn Initiator { get; set; }
        public Pawn Recipient { get; set; }
        public string Topic { get; set; }
        public int Priority { get; set; }
    }
    
    public class YourPawnState
    {
        public Pawn Pawn { get; }
        public Queue<TalkResponse> PendingDialogues { get; } = new();
        
        public YourPawnState(Pawn pawn)
        {
            Pawn = pawn;
        }
        
        public void AddPendingDialogue(TalkResponse response)
        {
            PendingDialogues.Enqueue(response);
        }
    }
}
```

### 模式 3: 扩展原始系统

扩展 RimTalk 的现有类而不是替换：

```csharp
using RimTalk.Service;
using RimTalk.Data;
using Verse;

namespace YourMod
{
    /// <summary>
    /// 扩展的对话服务，添加您的优化逻辑
    /// </summary>
    public static class EnhancedTalkService
    {
        /// <summary>
        /// 增强的对话生成，使用改进的逻辑
        /// </summary>
        public static bool GenerateTalkEnhanced(TalkRequest talkRequest)
        {
            // 您的预处理逻辑
            if (!PreProcessRequest(talkRequest))
                return false;
            
            // 调用原始的 TalkService（或您的实现）
            bool success = TalkService.GenerateTalk(talkRequest);
            
            // 您的后处理逻辑
            PostProcessRequest(talkRequest, success);
            
            return success;
        }
        
        private static bool PreProcessRequest(TalkRequest request)
        {
            // 您的新验证逻辑
            // 例如：更智能的时机检查、上下文感知等
            return true;
        }
        
        private static void PostProcessRequest(TalkRequest request, bool success)
        {
            // 您的新后处理逻辑
            // 例如：更新统计信息、触发事件等
        }
    }
}
```

## 最佳实践 (Best Practices)

### 1. 错误处理

```csharp
try
{
    var responses = await AIService.Chat(request, history);
    if (responses == null || responses.Count == 0)
    {
        Log.Warning("[YourMod] No responses received from LLM");
        return;
    }
    
    // 处理响应
}
catch (QuotaExceededException ex)
{
    Log.Error($"[YourMod] API quota exceeded: {ex.Message}");
    // 处理配额超限
}
catch (Exception ex)
{
    Log.Error($"[YourMod] Failed to generate dialogue: {ex}");
    // 处理其他错误
}
```

### 2. 性能优化

```csharp
// 避免在每个 tick 都提取游戏信息
private Dictionary<Pawn, (string context, int tick)> _contextCache = new();

private string GetCachedContext(Pawn pawn)
{
    if (_contextCache.TryGetValue(pawn, out var cached))
    {
        // 如果上下文在最近 N ticks 内，重用它
        if (GenTicks.TicksGame - cached.tick < 250) // ~4 秒
            return cached.context;
    }
    
    string context = PromptService.CreatePawnContext(pawn);
    _contextCache[pawn] = (context, GenTicks.TicksGame);
    return context;
}
```

### 3. 调试和日志

```csharp
// 启用详细日志以便调试
if (Settings.Get().DebugMode)
{
    Log.Message($"[YourMod] Generated prompt:\n{request.Prompt}");
    Log.Message($"[YourMod] Context:\n{context}");
}

// 使用 ApiHistory 跟踪 API 调用
// ApiHistory 会自动记录所有 AIService 调用
```

### 4. 兼容性

```csharp
// 检查 RimTalk 是否启用
if (!Settings.Get().IsEnabled)
{
    Log.Warning("[YourMod] RimTalk is disabled");
    return;
}

// 检查 API 配置
if (Settings.Get().GetActiveConfig() == null)
{
    Log.Warning("[YourMod] No active API configuration");
    return;
}
```

## 测试示例 (Testing Examples)

### 单元测试框架

```csharp
using NUnit.Framework;
using RimTalk.Service;
using RimTalk.Data;

namespace YourMod.Tests
{
    [TestFixture]
    public class IntegrationTests
    {
        [Test]
        public void TestPawnContextExtraction()
        {
            // 安排
            Pawn testPawn = CreateTestPawn();
            
            // 执行
            string context = PromptService.CreatePawnContext(
                testPawn, 
                PromptService.InfoLevel.Normal
            );
            
            // 断言
            Assert.IsNotNull(context);
            Assert.IsNotEmpty(context);
            Assert.That(context, Does.Contain(testPawn.LabelShort));
        }
        
        [Test]
        public async Task TestLLMCall()
        {
            // 安排
            var request = CreateTestRequest();
            var history = new List<(Role, string)>();
            
            // 执行
            var responses = await AIService.Chat(request, history);
            
            // 断言
            Assert.IsNotNull(responses);
            Assert.Greater(responses.Count, 0);
        }
        
        private Pawn CreateTestPawn()
        {
            // 创建测试 pawn 的辅助方法
            // ...
        }
        
        private TalkRequest CreateTestRequest()
        {
            // 创建测试请求的辅助方法
            // ...
        }
    }
}
```

## 常见问题 (FAQ)

### Q: 如何在不修改 RimTalk 源代码的情况下集成？

A: 使用 Facade 模式或创建包装类。RimTalk 的核心服务类是静态的，可以直接调用。

### Q: 如何处理 API 配额限制？

A: `AIService` 已经包含重试逻辑（通过 `AIErrorHandler`）。您可以：
- 监听 `QuotaExceededException`
- 实现请求节流
- 使用 `Stats` 类跟踪 API 使用情况

### Q: 如何自定义系统提示词？

A: 修改 `Constant.Instruction` 或在 mod 设置中使用自定义指令。您也可以在调用 `AIService.UpdateContext()` 时传入自己的指令。

### Q: 如何添加新的对话触发器？

A: 创建 Harmony patch 拦截游戏事件，然后调用您的对话系统。参考 RimTalk 的 `/Patch` 目录中的示例。

### Q: 如何优化性能？

A: 
1. 缓存游戏信息提取结果（几秒钟内不会改变）
2. 使用优先级队列管理请求
3. 限制同时进行的对话数量
4. 使用流式 API 以便更快的用户反馈

## 下一步 (Next Steps)

1. **设置开发环境** - 安装 RimWorld SDK 和构建工具
2. **创建您的 mod** - 引用 RimTalk 作为依赖
3. **实现 Facade** - 创建简化的 API 访问层
4. **测试集成** - 验证您可以调用 RimTalk 的功能
5. **构建您的逻辑** - 实现优化的对话系统
6. **迭代改进** - 基准测试和优化

祝您开发顺利！如果有任何问题，请参考 `ARCHITECTURE_ANALYSIS.md` 了解更多架构细节。
