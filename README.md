# RimTalk Manual & Developer Guide

RimTalk is an asynchronous AI dialogue and cognitive simulation framework for RimWorld 1.5 and 1.6 that intercepts game events, compiles colonist state and environmental context into sandboxed Scriban templates, queries large language model endpoints, and dispatches the parsed output into overhead speech bubbles, social relationship thoughts, and episodic memories.

---

## Table of Contents

- [1. Quick Setup & AI Provider Configuration](#1-quick-setup--ai-provider-configuration)
  - [1.1 Supported AI Providers](#11-supported-ai-providers)
  - [1.2 Three-Step Setup](#12-three-step-setup)
- [2. In-Depth Settings & Engine Architecture](#2-in-depth-settings--engine-architecture)
  - [2.1 Core Operational Settings](#21-core-operational-settings)
  - [2.2 Architecture: Condensed vs. Multi-Turn Dialogue History](#22-architecture-condensed-vs-multi-turn-dialogue-history)
  - [2.3 Fast-Track Interactions & Speech Bubbles](#23-fast-track-interactions--speech-bubbles)
- [3. Advanced Prompting & Scriban Templating Engine](#3-advanced-prompting--scriban-templating-engine)
  - [3.1 Scriban Architecture & Execution Model](#31-scriban-architecture--execution-model)
  - [3.2 Template Variable Dictionary](#32-template-variable-dictionary)
  - [3.3 Built-in Utility Functions & Static Game Objects](#33-built-in-utility-functions--static-game-objects)
  - [3.4 Working Scriban Prompt Preset Snippet](#34-working-scriban-prompt-preset-snippet)
- [4. Diagnostics: Debug Window & API Log Manual](#4-diagnostics-debug-window--api-log-manual)
  - [4.1 Accessing Diagnostics](#41-accessing-diagnostics)
  - [4.2 Four Diagnostic View Modes](#42-four-diagnostic-view-modes)
  - [4.3 Live Real-Time Token Monitor](#43-live-real-time-token-monitor)
  - [4.4 API Log Inspector (`Dialog_ApiLog`) & Live In-Game Resend](#44-api-log-inspector-dialog_apilog--live-in-game-resend)
- [5. Addon Modder Extensibility Guide (C# API)](#5-addon-modder-extensibility-guide-c-api)
  - [5.1 Soft-Dependency Integration](#51-soft-dependency-integration)
  - [5.2 Registering Custom Template Variables](#52-registering-custom-template-variables)
  - [5.3 Triggering Dialogue Programmatically (`AddTalkRequest`)](#53-triggering-dialogue-programmatically-addtalkrequest)
  - [5.4 Context Generation Hooks & Section Injections](#54-context-generation-hooks--section-injections)
  - [5.5 Preset Entry Registration & Built-in Toggles](#55-preset-entry-registration--built-in-toggles)
  - [5.6 Complete `RimTalkPromptAPI` Reference](#56-complete-rimtalkpromptapi-reference)

---

## 1. Quick Setup & AI Provider Configuration

### 1.1 Supported AI Providers

RimTalk connects to cloud inference endpoints and local servers supporting the OpenAI-compatible chat completion standard (`POST /v1/chat/completions`).

| Provider | Recommended Models | Endpoint URL | Authentication / Setup |
|---|---|---|---|
| **Google Gemini** *(Default Simple)* | `gemma-4-26b-a4b-it` (Default)<br>`gemini-2.5-flash` | `https://generativelanguage.googleapis.com/v1beta/openai/chat/completions` | Free API key from Google AI Studio. |
| **Player2** | Player2 Curated Models | `https://api.player2.game` (or local desktop proxy) | Zero-key auto-connect via local desktop application, or Web API key. |
| **OpenAI** | `gpt-4o-mini`, `gpt-4o`, `o3-mini` | `https://api.openai.com/v1/chat/completions` | Standard OpenAI API Key (`sk-...`). |
| **Anthropic Claude** | `claude-3-5-haiku`, `claude-3-5-sonnet` | `https://api.anthropic.com/v1/chat/completions` (via proxy or compatible gateway) | `x-api-key` header with `anthropic-version: 2023-06-01`. |
| **DeepSeek** | `deepseek-chat` (V3), `deepseek-reasoner` (R1) | `https://api.deepseek.com/v1/chat/completions` | DeepSeek Platform API Key. |
| **Grok (xAI)** | `grok-2` | `https://api.x.ai/v1/chat/completions` | xAI Platform API Key. |
| **Local LLMs (Ollama)** | `llama3.1:8b`, `qwen2.5:7b` | `http://localhost:11434/v1/chat/completions` | No key required. Run `ollama run <model>` locally. |
| **Local LLMs (LM Studio)** | Any loaded GGUF model | `http://localhost:1234/v1/chat/completions` | No key required. Start Local Server in LM Studio. |

### 1.2 Three-Step Setup

1. **Open Mod Settings**: Navigate in RimWorld to `Options -> Mod Settings -> RimTalk`.
2. **Select Connection Mode**: Choose a header card:
   - **Google Gemini**: Paste your free Google AI Studio key into the API key field.
   - **Player2**: Launch the Player2 desktop app for automatic linking, or paste your web key.
   - **Custom (Advanced)**: Configure custom OpenAI, Claude, DeepSeek, or local URL configurations.
3. **Verify Connection**:
   - For Cloud: Click `Verify / Fetch Models` to populate the model dropdown.
   - For Local (`Ollama` / `LM Studio`): Click `Auto Detect` or verify `localhost` port. Select your loaded model from the list.

---

## 2. In-Depth Settings & Engine Architecture

### 2.1 Core Operational Settings

Located in `Mod Settings -> RimTalk`. Visual settings (speech bubble fonts, padding, HUD opacity) are adjusted visually in-game with live previews. The table below lists the non-obvious operational controls governing API usage, state serialization, and stability:

| Setting Key | Label in UI | Default | Recommended | Description & Consequences |
|---|---|---|---|---|
| `TalkInterval` | AI Cooldown (Seconds) | `10` | `8`–`15` (Cloud)<br>`20`–`30` (Local) | Base cooldown between new dialogue requests. Lower values increase chat frequency at the cost of higher token consumption and rate-limit risks. |
| `ReplyInterval` | Reply Interval (Seconds) | `4` | `3`–`5` | Delay between consecutive speech bubbles within an active multi-turn conversation. |
| `UseCompactHistory` | Condensed Dialogue History | `false` (Standard)<br>`true` (Comp.) | `true` (Frontier)<br>`false` (8B Local) | Toggles between single-block condensed dialogue history (saves tokens, fixes monologue repetition) and native multi-turn message arrays. |
| `EnableContextOptimization` | Context Compression | `false` | `true` (Local 8B)<br>`false` (Cloud) | Truncates and compresses pawn profile strings to maximize inference throughput on local hardware. |
| `DisableAiAtSpeed` | Pause AI on High Speed | `0` (Disabled) | `3x` or `4x` | Automatically suspends AI request generation when running the game at 3x or 4x speed to prevent token drain. |
| `ProcessNonRimTalkInteractions` | Override All Interactions | `true` | `true` | When enabled, intercepts vanilla and modded social interactions (chitchat, slight, insult, romance) and regenerates them via AI. |
| `ApplyMoodAndSocialEffects` | Apply Mood & Social Effects | `false` | `true` (roleplay impact) | Parses structured JSON output (`"act"` and `"target"`) to apply in-game thoughts and relation adjustments (e.g., insults hurt relations). |
| `VersionSwitcher` | Previous Version Rollback | N/A | As needed | Emergency recovery tool in mod settings. Copies compiled assemblies from `LastVersion/` into a local non-Steam mod folder (`RimWorld/Mods/RimTalk`) to protect against Steam auto-updates. |

---

### 2.2 Architecture: Condensed vs. Multi-Turn Dialogue History

The `UseCompactHistory` toggle controls how historical conversation turns are structured when passed to the model:

```
[ Condensed History (Approach B') ]           [ Multi-Turn Array (Approach A) ]
Single User Message                           Alternating Message Array
┌──────────────────────────────────────┐     ┌───────────────────────────────────┐
│ [Chat History]                       │     │ User: [Situation & Prompt]        │
│ prompt: John insulted Sarah          │     ├───────────────────────────────────┤
│ John: "You're useless."              │     │ Assistant: {"name":"John", ...}   │
│ prompt: Sarah snapped back           │     ├───────────────────────────────────┤
│ Sarah: "Shut up and haul wood."      │     │ User: [Next Prompt]               │
│ [Situation]                          │     ├───────────────────────────────────┤
│ prompt: John prepares a retort       │     │ Assistant: {"name":"Sarah", ...}  │
└──────────────────────────────────────┘     └───────────────────────────────────┘
```

#### Technical Trade-offs

1. **Condensed History (Single Block with Causal Triggers — Default `{{ chat.history }}`)**:
   - **Token Economy**: Saves 30%–50% token overhead by stripping repeated system prompt headers, assistant role wrappers, and chat template markers (`<|im_start|>`, `[INST]`).
   - **Solves Monologue Repetition**: On frontier models (GPT-4o, Claude 3.5, Gemini), plain dialogue history causes pawns in solitary monologues to repeat previous lines. Injecting the causal trigger (`prompt: <event/topic>`) introduces varied wording that breaks repetitive loops.
   - **Preserves Causal Context in Arguments**: In conflicts (e.g. Pawn A insults Pawn B), plain history leads Pawn B to forget the insult and reset to a neutral tone. Retaining `prompt: A insulted B` forces the model to maintain dramatic tension.
   - **Transient Context Stripping**: Ambient data (`Nearby: Dead raider x1`) is automatically stripped before saving to history so pawns do not complain about buried corpses hours later.

2. **Multi-Turn Array (`chat.history_raw`)**:
   - Formats past dialogue as native alternating `user` and `assistant` JSON payload messages.
   - **Best Fit for Small Local Models (e.g. Llama-3-8B, Qwen-2.5-7B)**: Lightweight models often struggle to separate historical dialogue from current instructions within a single user block, occasionally echoing `prompt:` markers into their spoken output. Native multi-turn role demarcation guarantees strict format separation.

> For the detailed engineering decision log and test metrics, see [dialogue-history-architecture.md](Docs/dialogue-history-architecture.md).

---

### 2.3 Fast-Track Interactions & Speech Bubbles

- **Fast-Track Interactions (`Dialog_FastTrackInteractions`)**: Accessible via the gear icon next to `Override All Interactions`. Interactions selected here (e.g., `Chitchat`, `DeepTalk`, `Insult`, `RomanceAttempt`) trigger dialogue immediately, bypassing the standard `TalkInterval` cooldown.
- **Speech Bubbles & HUD Overlay**: Visual properties (Native vs. Interaction Bubbles, light/dark themes, 5pt–20pt font sizes, padding, background opacity, conversation group colors, urgent combat shaking, and floating history overlay) are configured directly via `Mod Settings -> RimTalk -> Speech Bubbles / Overlay` with real-time in-game preview rendering.

---

## 3. Advanced Prompting & Scriban Templating Engine

### 3.1 Scriban Architecture & Execution Model

RimTalk embeds a sandboxed implementation of the [.NET Scriban templating engine](https://github.com/scriban/scriban):
- **Main-Thread Execution**: Templates evaluate synchronously on the Unity main thread inside `ContextBuilder` and `PromptPresetAssembler`, ensuring thread-safe access to vanilla RimWorld world state (`Find.*`, `Pawn.*`, `Map.*`).
- **Syntax**: Uses Liquid/C# expressions enclosed in double braces `{{ ... }}`.
- **Reflection Safety**: Member access is case-insensitive. Methods returning `void` are blocked to prevent state corruption.

---

### 3.2 Template Variable Dictionary

| Variable | Scope | Type | Accessible Fields / Shorthands | Description |
|---|---|---|---|---|
| `{{ pawn }}` | Speaker | `Pawn` | `.LabelShort`, `.Name.ToStringFull`, `.gender`, `.age`, `.chronological_age`, `.race`, `.faction`, `.role`, `.job`, `.mental_state`, `.mood`, `.moodpercent`, `.personality`, `.traits`, `.skills`, `.health`, `.thoughts`, `.relations`, `.equipment`, `.ideology`, `.genes`, `.location`, `.terrain`, `.beauty`, `.cleanliness`, `.surroundings`, `.memory` | Initiator colonist. Accesses vanilla properties, health/skill trackers, and RimTalk magic context shorthands. |
| `{{ recipient }}` | Target | `Pawn` | Identical fields to `{{ pawn }}` | Pawn being spoken to. **Is `null` during monologues, announcements, or thoughts.** Always guard with `{{ if recipient }}`. |
| `{{ pawns }}` | Group | `List<Pawn>` | Indexing, `.Count` | List of all participating pawns. Iterate using `{{ for p in pawns }}`. |
| `{{ map }}` | Environment | `Map` | `.weather`, `.temperature`, `.wealth`, `.time`, `.date`, `.season`, `.events` | The active game map. Reflects environment hooks registered by mods. |
| `{{ game }}` | World Time | `ScriptObject` | `.time` ("3pm"), `.hour` (0–23), `.date`, `.day` (1–15), `.quadrum`, `.season`, `.year`, `.weather`, `.temperature`, `.wealth`, `.events` | Structured calendar and environment parameters. Root aliases exist for all fields (e.g. `{{ time }}`, `{{ weather }}`). |
| `{{ events }}` | Incidents | `string` | N/A | Formatted summary of active letters, raids, and threat notifications on the map. |
| `{{ chat.history }}` | History | `string` | N/A | Dialogue history compiled per settings (`chat.history_simplified` for condensed block, `chat.history_raw` for multi-turn array). |
| `{{ prompt }}` | Situation | `string` | N/A | Current interaction trigger text (e.g. "insulted by John", "downed in combat"). |
| `{{ context }}` | Identity | `string` | N/A | Formatted raw profile summary of the initiator (backstory, traits, health, thoughts). |
| `{{ memory }}` | Cognition | `string` | N/A | Reciprocal impressions and active directives between speaker and recipient. |
| `{{ json.format }}` | Anchor | `string` | N/A | Schema instructions enforcing JSONL output (`{"name": "...", "text": "..."}`). |
| `{{ json.anchor }}` | Anchor | `string` | N/A | Concise trailing JSON reinforcement anchor appended to the end of user input. |
| `{{ lang }}` | System | `string` | N/A | Active native language name (e.g. "English", "German"). |
| `{{ is_user }}` | Direction | `bool` | N/A | Returns `true` if dialogue involves the player character. |

> **Tip**: You can search and inspect all available objects, properties, and active third-party mod variables with live in-game values via **Mod Settings -> RimTalk -> Prompt Setting -> Reference**.

---

### 3.3 Built-in Utility Functions & Static Game Objects

Utility filters support pipe syntax (`{{ pawn | IsInCombat }}`) or standard function calls (`{{ IsInCombat pawn }}`):

| Function / Filter | Syntax Example | Return | Description |
|---|---|---|---|
| `IsInCombat` | `{{ pawn \| IsInCombat }}` | `bool` | Returns `true` if pawn is targeting or being attacked by enemies. |
| `IsInDanger` | `{{ IsInDanger pawn }}` | `bool` | Returns `true` if pawn is in combat, on fire, hypothermic, or in severe mental break. |
| `IsInCombatOrFire`| `{{ IsInCombatOrFire pawn }}` | `bool` | Returns `true` if pawn is under hostile fire or currently burning. |
| `IsInPainOrSick` | `{{ IsInPainOrSick pawn }}` | `bool` | Returns `true` if pain level > 15% or pawn has an active lethal disease. |
| `GetRole` | `{{ GetRole pawn }}` | `string` | Returns pawn settlement status: "Colonist", "Prisoner", "Slave", "Visitor", or "Enemy". |
| `IsEnemy` / `IsVisitor`| `{{ IsEnemy pawn }}` | `bool` | Checks faction allegiance. |
| `Sanitize` | `{{ prompt \| Sanitize }}` | `string` | Strips XML formatting tags, null characters, and unescaped quotes. |
| `random` | `{{ random 1 100 }}` | `int` | Returns a pseudo-random integer between min and max. |
| `setvar` / `getvar`| `{{ setvar "key" "val" }}` | `void` / `object`| Sets or gets temporary cross-entry variables within the same preset evaluation. |
| **Static Objects** | `Find.*`, `PawnsFinder.*`, `GenDate.*` | Native | Direct reflection into RimWorld engine singletons. |

---

### 3.4 Working Scriban Prompt Preset Snippet

Below is a production-ready template demonstrating conditional recipient branching, history injection, and structured JSON output:

```scriban
{{- # System Role - Rules & Identity -}}
Roleplay as the RimWorld colonist described below. Speak in {{ lang }} (1-2 short, grounded sentences).
{{ json.format }}

[Initiator Profile]
Name: {{ pawn.LabelShort }} | Role: {{ GetRole pawn }} | Health: {{ pawn.health }} | Mood: {{ pawn.mood }}
Traits: {{ pawn.traits }} | Thoughts: {{ pawn.thoughts }}
{{ if pawn.memory }}Episodic Memory: {{ pawn.memory }}{{ end }}

{{ if recipient }}
[Target Profile]
Name: {{ recipient.LabelShort }} | Role: {{ GetRole recipient }} | Health: {{ recipient.health }}
Social Standing: {{ pawn.relations }}
{{ else }}
(Speaking thoughts out loud alone)
{{ end }}

[Environment]
Time: {{ game.time }}, {{ game.season }} Year {{ game.year }} | Weather: {{ game.weather }}, {{ game.temperature }}°C
{{ if events }}[Incidents]: {{ events }}{{ end }}

{{- # User Role - History & Trigger -}}
{{ chat.history }}

[Current Situation]
{{ prompt }}

{{ json.anchor }}
```

---

## 4. Diagnostics: Debug Window & API Log Manual

### 4.1 Accessing Diagnostics

- **Shortcut Access**: `Ctrl + Left-Click` directly on the bottom-right RimTalk status toggle.
- **Main Bar**: Left-click the `RimTalkDebug` tab on the main bottom menu (when `ButtonDisplay` is set to `Tab`).

---

### 4.2 Four Diagnostic View Modes

Switch modes via the dropdown in the top-left toolbar:

1. **By Time (`DebugViewMode.MainTable`)**: Chronological audit table of all dispatched prompts. Displays timestamp, initiator pawn, interaction type (`Chitchat`, `Urgent`, `Thought`, `Hediff`, `Event`), round-trip latency (`Time(ms)`), token cost, and request state (`Generating`, `Spoken`, `Failed`, `Ignored`, `Expired`).
2. **By Pawn (`DebugViewMode.GroupedByPawn`)**: Aggregates metrics by individual colonist. Monitors talk initiation volume, chattiness settings, and cumulative token consumption to pinpoint runaway talk loops.
3. **TalkRequests (`DebugViewMode.ActiveRequests`)**: Live inspector for the scheduler queues (`PawnTalkScheduler` and `FastTrackDialogueScheduler`). Monitors `BusyGate` concurrency locks and pending multi-turn reply chains.
4. **Memories & Directives (`DebugViewMode.MemoryLog`)**: Real-time episodic memory ledger. Inspects interpersonal events (rescues, insults, witnessed kills), mathematical weights, half-life decay curves, permanent milestones (★), and active player directives.

---

### 4.3 Live Real-Time Token Monitor

The header panel features a live rolling 60-second performance monitor:
- **Meters**: Tracks `AI Status` (`Ready`, `Busy`, `Disabled`), cumulative session calls/tokens, rolling `Avg Calls/min`, `Avg Tokens/min`, `Avg Tokens/Call`, and instantaneous `Tokens/s`.
- **Bar Graph**: Blue bars represent **Prompt Tokens** (context, profiles, history); Green bars represent **Completion Tokens** (generated dialogue). Rapidly expanding blue bars indicate context bloat or excessive history lines.

---

### 4.4 API Log Inspector (`Dialog_ApiLog`) & Live In-Game Resend

Clicking any row in the Debug Window opens `Dialog_ApiLog`:
- **Payload Inspection**: The left pane shows the exact serialized JSON sent across the network (`Request`); the right pane shows the raw response JSON (`Response`), HTTP status code, and latency.
- **HTTP Error Traces**: Diagnoses `400 Bad Request`, `401 Unauthorized` (invalid API key), `429 Quota Exceeded`, or `5xx` provider outages.
- **Standard Resend**: Re-enqueues the request and queries the model using current game state.
- **Custom Edited Resend (Live Prompt Testing)**:
  1. Toggle **Edit** at the bottom of the Request pane.
  2. Modify prompt text, system rules, or context parameters directly in the text editor.
  3. The real-time validator confirms syntax (`✓ Valid JSON` or `⚠ Invalid JSON: syntax error`).
  4. Click **Resend**: RimTalk bypasses context compilation and dispatches the edited JSON payload immediately, allowing you to iterate on prompt wording live in-game without restarting.

---

## 5. Addon Modder Extensibility Guide (C# API)

RimTalk exposes its integration surface via the static class `RimTalk.API.RimTalkPromptAPI`.

### 5.1 Soft-Dependency Integration

To safely reference RimTalk without requiring it as a mandatory hard dependency:

```csharp
using Verse;

public static class RimTalkIntegration
{
    private static bool? _isLoaded;
    public static bool IsLoaded => _isLoaded ??= ModsConfig.IsActive("cj.rimtalk");

    public static void SafeRegisterVariables()
    {
        if (!IsLoaded) return;
        RegisterInternal();
    }

    // Isolate RimTalk type references to a method JIT-compiled only when RimTalk is active
    private static void RegisterInternal()
    {
        RimTalk.API.RimTalkPromptAPI.RegisterPawnVariable(
            modId: "yourmod.packageid",
            variableName: "mana",
            provider: pawn => "100/100",
            description: "Pawn magical energy reserve"
        );
    }
}
```

*(In your `.csproj`, mark `<Reference Include="RimTalk"><Private>false</Private></Reference>` so `RimTalk.dll` is not copied into your distribution output).*

---

### 5.2 Registering Custom Template Variables

#### 1. Pawn-Scoped Variables (`{{ pawn.<name> }}` and `{{ recipient.<name> }}`)
```csharp
RimTalkPromptAPI.RegisterPawnVariable(
    modId: "yourmod.packageid",
    variableName: "cyberware_tier",
    provider: pawn => pawn.health.hediffSet.hediffs.Any(h => !h.def.isBad) ? "Augmented" : "Pure",
    description: "Cybernetic augmentation level"
);
```

#### 2. Environment Variables (`{{ <name> }}`)
```csharp
RimTalkPromptAPI.RegisterEnvironmentVariable(
    modId: "yourmod.packageid",
    variableName: "mana_flux",
    provider: map => map.gameConditionManager.ConditionIsActive(GameConditionDef.Named("ManaStorm")) ? "Volatile" : "Stable",
    description: "Atmospheric mana instability"
);
```

#### 3. Context-Scoped Variables (`{{ <name> }}`)
Receives the full `PromptContext` (speaker, recipient, active topic, map):
```csharp
RimTalkPromptAPI.RegisterContextVariable(
    modId: "yourmod.packageid",
    variableName: "hostility_reason",
    provider: ctx => ctx.TalkRequest?.Recipient?.HostileTo(ctx.CurrentPawn) == true ? "Border dispute" : "None",
    description: "Reason for hostility between participants"
);
```

---

### 5.3 Triggering Dialogue Programmatically (`AddTalkRequest`)

To trigger dialogue on custom gameplay events, obtain the colonist's `PawnState` via `RimTalk.Data.Cache` and enqueue a `TalkRequest`:

```csharp
using RimTalk.Data;
using RimTalk.Source.Data;
using Verse;

public static class DialogueTrigger
{
    public static void TriggerSpellCast(Pawn caster, Pawn target, string spellName)
    {
        if (!ModsConfig.IsActive("cj.rimtalk") || caster == null) return;

        Cache.Get(caster)?.AddTalkRequest(
            prompt: $"cast {spellName} on {target?.LabelShort ?? "the area"}",
            recipient: target,
            talkType: TalkType.Urgent
        );
    }
}
```

#### `TalkType` Scheduling Reference

| `TalkType` Enum | Value | Scheduling Behavior | Primary Use Case |
|---|---|---|---|
| `TalkType.Urgent` | `0` | **Fast-Track Priority**: Evicts lower-priority queued requests; bypasses conversation cooldowns. | Combat callouts, emergency surgery, psychic shock, severe injury. |
| `TalkType.Hediff` | `1` | Enqueued to standard queue. | Infection, illness, trauma reaction. |
| `TalkType.LevelUp` | `2` | Enqueued to standard queue. | Skill advancement reaction. |
| `TalkType.Chitchat` | `3` | Subject to regular `TalkInterval` cooldown. | Idle social conversation. |
| `TalkType.Interaction` | `4` | Fast-tracked if enabled in Fast-Track Settings. | Vanilla social interaction override. |
| `TalkType.Event` | `5` | Enqueued to standard queue. | Colony incident, raid letter, solar flare reaction. |
| `TalkType.QuestOffer` | `6` | Enqueued to standard queue. | Quest generation or visitor proposal. |
| `TalkType.QuestEnd` | `7` | Enqueued to standard queue. | Quest completion or failure. |
| `TalkType.Thought` | `8` | Enqueued to standard queue. | Mood thought reaction (e.g. mental break warning). |
| `TalkType.User` | `9` | Highest priority; player-directed. | Direct player-to-colonist dialogue. |
| `TalkType.Announcement` | `10` | Broadcasts to all pawns in listening radius. | Colony-wide alarms, general announcements. |
| `TalkType.Sleep` | `11` | Restricted to bed transition ticks. | Bedtime whispers, nightmares, wake-up chatter. |
| `TalkType.Other` | `12` | Generic external trigger. | Third-party custom triggers. |

---

### 5.4 Context Generation Hooks & Section Injections

```csharp
using RimTalk.API;
using Verse;

// Append data to the existing Pawn Health context category
RimTalkPromptAPI.RegisterPawnHook(
    modId: "yourmod.packageid",
    category: ContextCategories.Pawn.Health,
    operation: ContextHookRegistry.HookOperation.Append,
    handler: (pawn, current) => current + $"; Cybernetic Strain: {GetStrain(pawn)}%"
);

// Inject a completely new section positioned after Pawn Traits
RimTalkPromptAPI.InjectPawnSection(
    modId: "yourmod.packageid",
    sectionName: "cult_devotion",
    anchor: ContextCategories.Pawn.Traits,
    position: ContextHookRegistry.InjectPosition.After,
    provider: pawn => $"Eldritch Devotion: {GetDevotionRank(pawn)}"
);

// Append data to Environment Weather
RimTalkPromptAPI.RegisterEnvironmentHook(
    modId: "yourmod.packageid",
    category: ContextCategories.Environment.Weather,
    operation: ContextHookRegistry.HookOperation.Append,
    handler: (map, currentWeather) => currentWeather + $" (Miasma Level: {GetMiasma(map)})"
);
```

---

### 5.5 Preset Entry Registration & Built-in Toggles

```csharp
using RimTalk.API;
using RimTalk.Prompt;

// Create and register a custom system rule entry
var entry = RimTalkPromptAPI.CreatePromptEntry(
    name: "Magic Lore Rules",
    content: "When discussing magic, speak with academic terminology and caution.",
    role: PromptRole.System,
    position: PromptPosition.Relative,
    sourceModId: "yourmod.packageid"
);

// Add to current active preset
RimTalkPromptAPI.AddPromptEntry(entry);

// Register entry to persist across user 'Reset with Mods' actions
RimTalkPromptAPI.RegisterModDefaultEntry(
    entry: entry,
    type: RimTalkPromptAPI.ModInsertionType.AfterId,
    targetId: BuiltInPromptIds.BaseInstruction
);

// If your mod supplies its own dialogue history formatter, disable RimTalk's built-in history
RimTalkPromptAPI.SetBuiltInEntryEnabled("yourmod.packageid", BuiltInPromptIds.ChatHistory, false);
```

---

### 5.6 Complete `RimTalkPromptAPI` Reference

| Method Signature | Return | Description |
|---|---|---|
| `RegisterPawnVariable(string modId, string variableName, Func<Pawn, string> provider, string description = null, int priority = 100)` | `void` | Registers a pawn-scoped variable accessible as `{{ pawn.<name> }}` and `{{ recipient.<name> }}`. |
| `RegisterEnvironmentVariable(string modId, string variableName, Func<Map, string> provider, string description = null, int priority = 100)` | `void` | Registers an environmental variable accessible at root template scope as `{{ <name> }}`. |
| `RegisterContextVariable(string modId, string variableName, Func<PromptContext, string> provider, string description = null, int priority = 100)` | `void` | Registers a context variable receiving full `PromptContext`, accessible as `{{ <name> }}`. |
| `GetRegisteredCustomVariables()` | `IEnumerable<(string, string, string, string)>` | Returns all custom variables registered across active mods. |
| `RegisterModDefaultEntry(PromptEntry entry, ModInsertionType type = ModInsertionType.Add, int index = -1, string targetId = null, string targetName = null)` | `void` | Registers a template prompt entry included when presets are reset to mod defaults. |
| `SetBuiltInEntryEnabled(string modId, string targetIdOrName, bool enabled)` | `bool` | Enables or disables a canonical built-in prompt entry (e.g. `chat-history`, `recent-events`). |
| `HasRegisteredModDefaults()` | `bool` | Returns `true` if any loaded mod has registered default prompt entries or modified built-in states. |
| `ApplyRegisteredModDefaults(PromptPreset targetPreset)` | `void` | Applies registered mod entries and overrides onto the specified target preset. |
| `AddPromptEntry(PromptEntry entry)` | `bool` | Appends a `PromptEntry` to the currently active preset. |
| `InsertPromptEntry(PromptEntry entry, int index)` | `bool` | Inserts a `PromptEntry` at a 0-based index in the active preset. |
| `InsertPromptEntryAfter(PromptEntry entry, string afterEntryId)` | `bool` | Inserts a `PromptEntry` after the entry matching the given ID in the active preset. |
| `InsertPromptEntryBefore(PromptEntry entry, string beforeEntryId)` | `bool` | Inserts a `PromptEntry` before the entry matching the given ID in the active preset. |
| `InsertPromptEntryAfterName(PromptEntry entry, string afterEntryName)` | `bool` | Inserts a `PromptEntry` after the first entry matching display name in active preset. |
| `InsertPromptEntryBeforeName(PromptEntry entry, string beforeEntryName)` | `bool` | Inserts a `PromptEntry` before the first entry matching display name in active preset. |
| `FindEntryIdByName(string entryName)` | `string` | Returns unique ID of a prompt entry matching display name in active preset, or `null`. |
| `RemovePromptEntry(string entryId)` | `bool` | Removes a prompt entry from the active preset by its ID. |
| `RemovePromptEntriesByModId(string modId)` | `int` | Removes all prompt entries originating from the specified mod ID. Returns count removed. |
| `GetVariableStore()` | `VariableStore` | Returns global variable store instance for persistent template key-value pairs. |
| `SetGlobalVariable(string key, string value)` | `void` | Stores a persistent key-value pair in global store, usable in templates as `{{ <key> }}`. |
| `GetGlobalVariable(string key, string defaultValue = "")` | `string` | Retrieves a persistent global variable by key. |
| `GetActivePreset()` | `PromptPreset` | Returns currently active `PromptPreset` instance. |
| `GetAllPresets()` | `IReadOnlyList<PromptPreset>` | Returns read-only list of all available prompt presets. |
| `RegisterPawnHook(string modId, ContextCategory category, ContextHookRegistry.HookOperation operation, Func<Pawn, string, string> handler, int priority = 100)` | `void` | Registers a hook to modify pawn context generation (`Append`, `Prepend`, or `Override`). |
| `RegisterEnvironmentHook(string modId, ContextCategory category, ContextHookRegistry.HookOperation operation, Func<Map, string, string> handler, int priority = 100)` | `void` | Registers a hook to modify map environment context generation. |
| `InjectPawnSection(string modId, string sectionName, ContextCategory anchor, ContextHookRegistry.InjectPosition position, Func<Pawn, string> provider, int priority = 100)` | `void` | Injects a new pawn context section positioned relative to an anchor category. |
| `InjectEnvironmentSection(string modId, string sectionName, ContextCategory anchor, ContextHookRegistry.InjectPosition position, Func<Map, string> provider, int priority = 100)` | `void` | Injects a new environment context section positioned relative to an anchor category. |
| `UnregisterAllHooks(string modId)` | `void` | Removes all hooks and injected sections registered by the specified mod ID. |
| `HasAnyHooks()` | `bool` | Returns `true` if any hooks or injected sections are currently active. |
| `CreatePromptEntry(string name, string content, PromptRole role = PromptRole.System, PromptPosition position = PromptPosition.Relative, int inChatDepth = 0, string sourceModId = null)` | `PromptEntry` | Factory method to instantiate and initialize a `PromptEntry`. |
