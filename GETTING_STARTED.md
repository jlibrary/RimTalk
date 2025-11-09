# 开始指南 (Getting Started Guide)

> **快速参考**：如何使用这些文档来开始您的 RimTalk 优化项目

## 您问的问题

> "这是一个 rimworld mod，我正在写一个用途类似但工程实现更优的 mod。我需要保留它调用 LLM 的实现和 UI，了解它'从游戏内提取信息'和'生成最终的提示词'这两个端点是如何工作的，而中间处理游戏状态数据的所有逻辑将被替换成我的实现。请你分析我该如何开始。"

## 答案总结

我已经完成了完整的分析，创建了三份详细文档帮助您理解架构并开始集成。

### 📚 文档阅读顺序

#### 第一步：理解架构
**阅读：`ARCHITECTURE_ANALYSIS.md`**

这份文档详细分析了：
- ✅ **第一个端点：游戏信息提取**
  - `PromptService.BuildContext()` - 如何提取多个 pawn 的上下文
  - `PromptService.CreatePawnContext()` - 如何提取单个 pawn 的完整信息
  - `PawnService` 的辅助方法 - 角色、状态、危险评估等
  
- ✅ **第二个端点：最终提示词生成**
  - `PromptService.DecoratePrompt()` - 如何将游戏上下文组合成最终提示词
  - 添加环境信息（时间、天气、位置等）

- ✅ **LLM 调用与 UI（要保留）**
  - `AIService` - 如何调用 LLM API
  - `AIClientFactory` - 多提供商支持
  - UI 组件 - 用户交互

- ✅ **中间处理层（需要替换）**
  - `TalkService` - 对话编排逻辑
  - 状态管理 - `Cache`, `PawnState`, `TalkHistory`
  - 队列管理 - `TalkRequestPool`

#### 第二步：学习如何调用
**阅读：`INTEGRATION_GUIDE.md`**

这份文档提供了：
- 🔧 **具体的代码示例**
  - 如何调用游戏信息提取 API
  - 如何调用 LLM（流式和非流式）
  - 如何处理用户自定义对话

- 🏗️ **三种集成模式**
  - **Facade 模式**：创建简化的访问层（推荐起步）
  - **自定义系统**：完全替换中间层
  - **扩展模式**：增强现有功能

- 💡 **最佳实践**
  - 错误处理
  - 性能优化
  - 调试技巧

#### 第三步：理解数据流
**阅读：`DATA_FLOW_DIAGRAMS.md`**

这份文档包含：
- 📊 **可视化流程图**
  - 完整对话生成流程（从游戏事件到显示）
  - 用户自定义对话流程
  - 状态管理架构
  
- 🔄 **数据模型关系**
  - 所有数据结构如何关联
  - 哪些保留、哪些替换的清晰图示

- ✅ **集成检查清单**
  - 6 个阶段的完整任务列表

## 🚀 快速开始步骤

### 1. 环境准备（5 分钟）

```bash
# 克隆仓库
git clone https://github.com/suzvka/RimTalk.git
cd RimTalk

# 构建项目（验证环境）
dotnet build -c Release -p:EnableBubbles=false

# 应该看到 "Build succeeded"
```

### 2. 理解核心端点（30 分钟）

打开 `ARCHITECTURE_ANALYSIS.md`，重点阅读：
- "第一个端点：游戏信息提取"部分
- "第二个端点：最终提示词生成"部分
- "LLM 调用与 UI"部分

**关键文件位置：**
- `Source/Service/PromptService.cs` - 信息提取和提示词生成
- `Source/Service/PawnService.cs` - Pawn 状态和角色判断
- `Source/Service/AIService.cs` - LLM API 调用

### 3. 运行示例代码（1 小时）

打开 `INTEGRATION_GUIDE.md`，尝试：

**示例 1：提取 Pawn 信息**
```csharp
// 从文档复制这段代码
Pawn myPawn = ...; // 从游戏中获取
string context = PromptService.CreatePawnContext(myPawn, PromptService.InfoLevel.Normal);
Console.WriteLine(context);
```

**示例 2：调用 LLM（非流式）**
```csharp
// 从文档的"非流式调用"部分复制完整代码
// 理解如何准备请求、调用 API、处理响应
```

### 4. 设计您的架构（2-3 小时）

根据 `INTEGRATION_GUIDE.md` 中的三种模式，选择一种：

**推荐：从 Facade 模式开始**

1. 创建新文件：`YourMod/RimTalkCore.cs`
2. 复制文档中的 Facade 类代码
3. 验证可以调用所有核心功能
4. 开始实现您的对话编排逻辑

### 5. 实现您的逻辑（逐步进行）

按照 `DATA_FLOW_DIAGRAMS.md` 中的"集成检查清单"：

**阶段 1: 准备** ✅
- 已完成：理解代码、设置环境、构建项目

**阶段 2: 隔离**
- 创建接口层包装核心功能
- 验证接口层不破坏原有功能

**阶段 3: 实现**
- 实现您的对话编排逻辑
- 实现您的状态管理系统
- 实现您的队列管理系统

**阶段 4-6: 测试、优化、发布**
- 逐步进行，参考检查清单

## 🎯 核心概念速查

### 保留这些（直接调用）

```csharp
// 游戏信息提取
string context = PromptService.BuildContext(pawns);
string pawnInfo = PromptService.CreatePawnContext(pawn, level);
var (status, danger) = pawn.GetPawnStatusFull(nearbyPawns);

// 提示词生成
PromptService.DecoratePrompt(request, pawns, status);

// LLM 调用
AIService.UpdateContext(context);
var responses = await AIService.Chat(request, history);
// 或流式：
await AIService.ChatStreaming(request, history, playerDict, callback);

// UI
Find.WindowStack.Add(new CustomDialogueWindow(initiator, recipient));
```

### 替换这些（您的新实现）

```csharp
// 不要直接使用这些，实现您自己的版本：
TalkService.GenerateTalk()           // → 您的对话决策逻辑
PawnSelector.GetAllNearByPawns()     // → 您的参与者选择
Cache.Get(pawn)                       // → 您的状态管理
TalkHistory.GetMessageHistory()      // → 您的历史管理
```

### 数据契约（保持不变）

```csharp
// 这些是接口边界，不要修改
TalkRequest   // 输入数据结构
TalkResponse  // 输出数据结构
TalkType      // 对话类型枚举
Role          // 消息角色（System/User/Assistant）
```

## 📖 常见场景示例

### 场景 1：我想触发一个对话

```csharp
// 1. 准备参与者
Pawn initiator = ...;
Pawn recipient = ...;
List<Pawn> participants = new List<Pawn> { initiator, recipient };

// 2. 提取游戏信息（使用 RimTalk 的）
string context = PromptService.BuildContext(participants);
var (status, _) = initiator.GetPawnStatusFull(PawnSelector.GetAllNearByPawns(initiator));

// 3. 创建并装饰请求（使用 RimTalk 的）
var request = new TalkRequest("discussing the weather", initiator, recipient);
PromptService.DecoratePrompt(request, participants, status);

// 4. 调用 LLM（使用 RimTalk 的）
AIService.UpdateContext(context);
var history = TalkHistory.GetMessageHistory(initiator);
var responses = await AIService.Chat(request, history);

// 5. 处理响应（使用您的逻辑）
foreach (var response in responses)
{
    // 您的队列管理、状态更新等
    YourQueueManager.Enqueue(response);
}
```

### 场景 2：我想优化对话触发逻辑

```csharp
// 不要修改 ThoughtPatch 等 Harmony patches
// 而是创建您自己的决策层

public class YourConversationManager
{
    public bool ShouldTriggerConversation(Pawn pawn, string trigger)
    {
        // 您的新逻辑：
        // - 更智能的时机判断
        // - 优先级计算
        // - 上下文相关性分析
        // ...
        
        if (shouldTrigger)
        {
            // 仍然使用 RimTalk 的核心功能
            TriggerConversation(pawn);
        }
    }
    
    private void TriggerConversation(Pawn pawn)
    {
        // 调用 RimTalk 的游戏信息提取和 LLM
        // （参考场景 1）
    }
}
```

### 场景 3：我想更高效地管理状态

```csharp
// 不要直接使用 PawnState 和 Cache
// 创建您自己的状态管理

public class YourStateManager
{
    private ConcurrentDictionary<Pawn, YourPawnState> _states = new();
    
    public YourPawnState GetState(Pawn pawn)
    {
        return _states.GetOrAdd(pawn, p => new YourPawnState(p));
    }
    
    // 但是仍然使用 RimTalk 的信息提取
    public void UpdateContext(Pawn pawn)
    {
        var state = GetState(pawn);
        state.CachedContext = PromptService.CreatePawnContext(pawn);
        state.LastUpdate = DateTime.Now;
    }
}
```

## 💡 重要提示

### ✅ 这样做：

1. **保留原始方法**：直接调用 `PromptService`、`AIService` 的方法
2. **创建包装层**：用 Facade 或接口隔离核心功能
3. **实现您的逻辑**：在包装层之上构建您的系统
4. **测试每一步**：确保不破坏原有功能

### ❌ 不要这样做：

1. **不要修改 RimTalk 源码**：保持其核心功能不变
2. **不要直接依赖 TalkService**：它是要被替换的部分
3. **不要改变数据契约**：`TalkRequest`/`TalkResponse` 是接口边界
4. **不要跳过文档**：按顺序阅读会节省很多时间

## 🔍 调试技巧

### 启用详细日志

```csharp
// 在 RimWorld mod 设置中启用 RimTalk 的调试模式
if (Settings.Get().DebugMode)
{
    Log.Message($"[YourMod] Context: {context}");
    Log.Message($"[YourMod] Prompt: {request.Prompt}");
}
```

### 查看 API 调用历史

```csharp
// RimTalk 自动记录所有 API 调用
var logs = ApiHistory.GetAll();
foreach (var log in logs)
{
    Log.Message($"Request: {log.Request.Prompt}");
    Log.Message($"Response: {log.Response}");
    Log.Message($"Tokens: {log.Payload?.TokenCount}");
}
```

### 断点调试关键位置

在这些位置设置断点来理解流程：
1. `PromptService.BuildContext()` - 观察如何提取信息
2. `PromptService.DecoratePrompt()` - 观察如何生成最终提示词
3. `AIService.Chat()` - 观察 LLM 调用
4. `TalkService.GenerateTalk()` - 观察原始决策逻辑（您要替换的）

## 📞 需要帮助？

如果遇到问题：

1. **重新阅读相关文档部分**
   - 架构问题 → `ARCHITECTURE_ANALYSIS.md`
   - 代码问题 → `INTEGRATION_GUIDE.md`
   - 流程问题 → `DATA_FLOW_DIAGRAMS.md`

2. **检查示例代码**
   - 所有文档都包含可运行的代码示例
   - 从最简单的示例开始

3. **参考原始实现**
   - 查看 RimTalk 的源码了解细节
   - 但不要复制中间层的逻辑（那是要替换的）

## 🎓 学习路径建议

### 初学者路径（推荐）
1. ✅ 阅读 `ARCHITECTURE_ANALYSIS.md` 前半部分
2. ✅ 运行 `INTEGRATION_GUIDE.md` 中的"示例 1-3"
3. ✅ 创建简单的 Facade 类
4. ✅ 实现一个简单的触发逻辑
5. ✅ 逐步添加功能

### 高级路径
1. ✅ 通读所有三份文档
2. ✅ 理解完整的数据流（`DATA_FLOW_DIAGRAMS.md`）
3. ✅ 设计完整的新架构
4. ✅ 一次性实现所有替换
5. ✅ 性能优化和压力测试

## 📊 时间估算

- **理解架构**：2-3 小时（阅读文档）
- **环境准备**：1 小时（设置开发环境、构建项目）
- **创建接口层**：2-4 小时（Facade 类、验证）
- **实现新逻辑**：1-2 周（取决于复杂度）
- **测试优化**：3-5 天（全面测试、性能优化）

## ✨ 总结

您现在拥有：
- ✅ **完整的架构分析**：知道保留什么、替换什么
- ✅ **实用的代码示例**：可以直接运行和参考
- ✅ **清晰的数据流程**：理解整个系统如何工作
- ✅ **详细的集成指南**：一步步的实施计划

**下一步**：选择一个集成模式，创建您的第一个 Facade 类，开始逐步实现！

祝您开发顺利！🚀
