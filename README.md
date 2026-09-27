# RimTalk

Ever wonder what your colonists are actually talking about when they chat around the campfire or pass each other in the hallway?

RimTalk replaces the repetitive canned interactions in RimWorld with real conversations. Every line of dialogue is generated on the fly by an LLM, shaped by each colonist's backstory, traits, mood, health, relationships, and whatever is happening around the colony. A grumpy brawler vents about eating without a table. An exhausted cook brags about a simple meal. A sarcastic doctor checks in on a patient they barely tolerate. Colonists speak with distinct voices because the mod feeds the model everything it needs to produce them.

Generated dialogue shows up as overhead speech bubbles and gets recorded into social logs and episodic memory. The entire AI pipeline runs asynchronously off the main thread, so the game never hitches while waiting for a response.

## How It Works

When a social interaction fires (chitchat, insult, romance attempt, mental break, or any modded interaction), RimTalk intercepts it and runs it through a four-stage pipeline:

1. **Intercept**: Harmony patches catch vanilla and modded social interactions. The mod checks cooldowns, busy state, and whether the pawn is eligible to talk.

2. **Build context (main thread)**: `ContextBuilder` reads live game state and assembles a text profile of each participant. This runs synchronously on Unity's main thread because RimWorld's APIs (`Pawn.*`, `Map.*`, `Find.*`) are not thread-safe. The profile includes everything listed in [Context Categories](#context-categories) below, filtered by user settings.

3. **Generate dialogue (background thread)**: The assembled prompt is sent to your configured API endpoint via `Task.Run`. Network I/O happens entirely off the main thread. The game keeps ticking normally.

4. **Display and record (main thread)**: When the response comes back, the mod parses it back on the main thread. The spoken text appears as a speech bubble, an `RimTalkInteraction` entry goes into the social log, and if mood/social effects are enabled, thoughts and opinion changes apply. Significant exchanges get saved to the pawn's episodic memory.

### Fast-tracking

Some interaction types skip the normal cooldown timer entirely: `User` (player-directed dialogue), `Announcement` (colony-wide broadcasts), `Interaction` (vanilla social events configured in Fast-Track settings), and `Urgent` (combat, danger). These can also preempt a currently generating request if it is lower priority.

### Episodic Memory

Each pawn maintains up to 25 memories with weighted half-life decay (default 4 days, 7 days for core traumas). Memories include interpersonal events (rescues, insults, witnessed kills), permanent milestones, and player-issued directives. A 5-hour debounce window prevents duplicate entries from rapid-fire events. These memories are injected into the prompt so colonists recall past interactions naturally.

## Context Categories

RimTalk pulls the following data from the game to build each prompt. Every category can be toggled on or off in **Mod Settings > RimTalk > Context Filter**.

**Pawn**:
Name, gender, biological and chronological age, xenotype, notable genes, backstory (childhood + adulthood), traits with descriptions, ideology and precepts, faction, role (colonist/prisoner/slave/visitor), current job, skill proficiencies (grouped by tier), health conditions (injuries, diseases, pain level, bleeding, implants, missing parts), mood percentage, active mental state, recent thoughts, social relations and opinion scores, equipped weapon and apparel, and episodic memories.

**Environment**:
Time of day, date, quadrum, season, year, outdoor temperature, active weather, room role, room beauty, room cleanliness, terrain, nearby surroundings, and colony wealth.

**Incidents**:
Active raids, psychic phenomena, environmental threats (solar flares, toxic fallout, cold snaps), quest letters, and map-level notifications.

## Quick Start

1. Open **Options > Mod Settings > RimTalk**.
2. Pick a connection mode:
   * **Google Gemini**: Paste a free API key from Google AI Studio.
   * **Player2**: Launch the Player2 desktop app for auto-connect, or paste your key.
   * **Custom**: Point to OpenAI, Claude, DeepSeek, Grok, OpenRouter, or a local server (Ollama at `localhost:11434`, LM Studio at `localhost:1234`).
3. Click **Verify / Fetch Models**, pick a model from the dropdown.

Any endpoint that supports the OpenAI chat completion format (`POST /v1/chat/completions`) will work.

## Non-Human Dialogue

Human colonists talk by default. Animals, mechanoids, and ghouls cannot speak unless you administer a **Vocal Link Catalyst** (`VocalLinkCatalyst`) to them. This is a single-use item (found under the Drugs category) that installs the `VocalLinkImplant` hediff, enabling dialogue, speech bubbles, and memory tracking for that creature.

## Notable Settings

All settings are in **Mod Settings > RimTalk**. Most are self-explanatory. These are the ones worth knowing about:

* **Override All Interactions** (`ProcessNonRimTalkInteractions`): When on, vanilla and modded social interactions get intercepted and regenerated through AI. The gear icon next to this opens **Fast-Track Interactions**, where you pick which interaction types bypass the cooldown.
* **Apply Mood & Social Effects** (`ApplyMoodAndSocialEffects`): Lets AI-generated dialogue apply in-game mood thoughts (`RimTalk_Chitchat`, `RimTalk_KindWords`, `RimTalk_Slighted`) and shift relationship opinions.
* **Pause AI on High Speed** (`DisableAiAtSpeed`): Suspends dialogue generation at 3x or 4x speed to avoid burning tokens while fast-forwarding.
* **Condensed Dialogue History** (`UseCompactHistory`): Packs past conversation turns into a single text block to save tokens. Better for cloud models. Local models sometimes echo the `prompt:` markers, so switch to multi-turn format (`chat.history_raw`) for those. Details in [dialogue-history-architecture.md](Docs/dialogue-history-architecture.md).
* **Context Compression** (`EnableContextOptimization`): Truncates pawn profiles to fit smaller context windows. Useful for local models.
* **Version Rollback** (`VersionSwitcher`): Copies assemblies from `LastVersion/` into a local mod folder to recover from bad Steam updates.

## Diagnostics

`Ctrl + Left-Click` the RimTalk toggle in the bottom-right corner to open the debug window. Four view modes:

* **By Time**: Chronological log of every prompt. Shows timestamp, pawn, interaction type, round-trip latency, token cost, and status (Generating, Spoken, Failed, Expired).
* **By Pawn**: Token usage and chat frequency aggregated per colonist. Useful for spotting runaway chatter loops.
* **Active Requests**: Live view of pending request queues and multi-turn reply chains.
* **Memory Log**: Episodic memory inspector showing interpersonal events, weights, half-life decay, milestones, and player directives.

The header panel shows a rolling 60-second token graph (blue = prompt tokens, green = completion tokens). Expanding blue bars mean context bloat.

Click any row to open the **API Log** (`Dialog_ApiLog`). Left pane shows the exact JSON sent to the API. Right pane shows the raw response, HTTP status, and latency. You can toggle **Edit** mode, modify the prompt text, validate JSON in real time, and hit **Resend** to test prompt changes live without restarting.

## Prompt Customization (Scriban)

RimTalk uses the [Scriban](https://github.com/scriban/scriban) template engine. Templates evaluate synchronously on the main thread inside `PromptPresetAssembler`, so they have safe access to all RimWorld game objects.

### Variables

| Variable | What it is |
|---|---|
| `{{ pawn }}` | The speaking colonist. Fields: `.LabelShort`, `.traits`, `.skills`, `.health`, `.mood`, `.thoughts`, `.relations`, `.memory`, `.equipment`, `.genes`, `.ideology`, `.location`, `.surroundings` |
| `{{ recipient }}` | Who they are talking to. Same fields as `pawn`. Null during monologues, always guard with `{{ if recipient }}`. |
| `{{ pawns }}` | List of all participants. Iterate with `{{ for p in pawns }}`. |
| `{{ game }}` | World clock: `.time`, `.hour`, `.date`, `.day`, `.quadrum`, `.season`, `.year`, `.weather`, `.temperature`, `.wealth` |
| `{{ events }}` | Formatted summary of active raids, threats, and letters on the map. |
| `{{ chat.history }}` | Condensed dialogue history (default). Use `{{ chat.history_raw }}` for multi-turn format. |
| `{{ prompt }}` | The trigger text ("insulted by John", "just woke up", "quest completed"). |
| `{{ context }}` | Full formatted profile of the speaker. |
| `{{ memory }}` | Reciprocal impressions and directives between speaker and recipient. |
| `{{ json.format }}` | Output schema instructions for structured JSON response. |
| `{{ json.anchor }}` | Trailing JSON reinforcement appended to user input. |
| `{{ lang }}` | Active game language name ("English", "Korean", "German"). |
| `{{ is_user }}` | True if the player character is involved. |

Browse all available variables (including ones registered by other mods) with live in-game values: **Mod Settings > RimTalk > Prompt Setting > Reference**.

### Filters

* `{{ pawn | IsInCombat }}` : true if fighting or targeted by hostiles.
* `{{ pawn | IsInDanger }}` : true during combat, fire, hypothermia, or severe mental break.
* `{{ pawn | GetRole }}` : returns "Colonist", "Prisoner", "Slave", "Visitor", or "Enemy".
* `{{ prompt | Sanitize }}` : strips XML tags, null characters, and unescaped quotes.
* `{{ random 1 100 }}` : random integer between min and max.

### Example Template

```scriban
Roleplay as the RimWorld colonist described below. Speak in {{ lang }} (1-2 short sentences).
{{ json.format }}

[Initiator]
Name: {{ pawn.LabelShort }} ({{ GetRole pawn }})
Health: {{ pawn.health }} | Mood: {{ pawn.mood }}
Traits: {{ pawn.traits }} | Thoughts: {{ pawn.thoughts }}
{{ if pawn.memory }}Memories: {{ pawn.memory }}{{ end }}

{{ if recipient }}
[Target]
Name: {{ recipient.LabelShort }} ({{ GetRole recipient }})
Relations: {{ pawn.relations }}
{{ else }}
(Talking to themselves)
{{ end }}

[Environment]
Time: {{ game.time }}, {{ game.season }} Year {{ game.year }} | Weather: {{ game.weather }}
{{ if events }}Events: {{ events }}{{ end }}

{{ chat.history }}

[Current Situation]
{{ prompt }}

{{ json.anchor }}
```

## Modder API

Third-party mods integrate with RimTalk through `RimTalk.API.RimTalkPromptAPI`. Reference `RimTalk.dll` with `<Private>false</Private>` in your `.csproj` so the DLL does not ship with your mod.

### Soft Dependency Setup

Isolate all RimTalk type references behind a runtime check so your mod loads fine without RimTalk installed:

```csharp
using Verse;

public static class RimTalkCompat
{
    public static bool IsActive => ModsConfig.IsActive("cj.rimtalk");

    public static void Initialize()
    {
        if (!IsActive) return;
        RegisterInternal();
    }

    // JIT-compiled only when RimTalk is actually loaded
    private static void RegisterInternal()
    {
        RimTalk.API.RimTalkPromptAPI.RegisterPawnVariable(
            modId: "yourmod.packageid",
            variableName: "magic_energy",
            provider: pawn => "100/100",
            description: "Current mana pool"
        );
    }
}
```

### Custom Template Variables

```csharp
// Pawn-scoped: accessible as {{ pawn.cyberware }} and {{ recipient.cyberware }}
RimTalkPromptAPI.RegisterPawnVariable(
    modId: "yourmod.packageid",
    variableName: "cyberware",
    provider: pawn => HasAugments(pawn) ? "Augmented" : "Natural"
);

// Environment-scoped: accessible as {{ radiation }}
RimTalkPromptAPI.RegisterEnvironmentVariable(
    modId: "yourmod.packageid",
    variableName: "radiation",
    provider: map => GetRadiationLevel(map).ToString()
);

// Context-scoped: receives the full PromptContext (speaker, recipient, topic, map)
RimTalkPromptAPI.RegisterContextVariable(
    modId: "yourmod.packageid",
    variableName: "hostility_reason",
    provider: ctx => ctx.TalkRequest?.Recipient?.HostileTo(ctx.CurrentPawn) == true
        ? "Border dispute" : "None"
);
```

### Triggering Dialogue

```csharp
using RimTalk.Data;
using Verse;

Cache.Get(caster)?.AddTalkRequest(
    prompt: $"cast {spellName} on {target?.LabelShort ?? "the area"}",
    recipient: target
);
```

### Context Hooks

Append, prepend, or override sections of the generated context:

```csharp
using RimTalk.API;

// Append to pawn health context
RimTalkPromptAPI.RegisterPawnHook(
    modId: "yourmod.packageid",
    category: ContextCategories.Pawn.Health,
    operation: ContextHookRegistry.HookOperation.Append,
    handler: (pawn, current) => current + $"; Infection Risk: {GetRisk(pawn)}%"
);

// Inject an entirely new section after Traits
RimTalkPromptAPI.InjectPawnSection(
    modId: "yourmod.packageid",
    sectionName: "cult_rank",
    anchor: ContextCategories.Pawn.Traits,
    position: ContextHookRegistry.InjectPosition.After,
    provider: pawn => $"Cult Rank: {GetRank(pawn)}"
);
```

### Prompt Entry Registration

Register prompt entries that persist across preset resets:

```csharp
var entry = RimTalkPromptAPI.CreatePromptEntry(
    name: "Magic Lore Rules",
    content: "When discussing magic, use academic terminology.",
    role: PromptRole.System,
    sourceModId: "yourmod.packageid"
);
RimTalkPromptAPI.RegisterModDefaultEntry(entry);
```

## Compatibility

RimTalk adds the following Defs. If you run mods that rewrite social systems, these are the touch points to check:

* `InteractionDef`: `RimTalkInteraction`
* `ThingDef`: `VocalLinkCatalyst`
* `HediffDef`: `VocalLinkImplant`, `RimTalk_PersonaData`
* `ThoughtDef`: `RimTalk_Chitchat`, `RimTalk_KindWords`, `RimTalk_Slighted`
* `JobDef`: `ApplyVocalLinkCatalyst`
* `MainButtonDef`: `RimTalkDebug`

## Credits

Development: Juicy

License: CC BY-NC-SA 4.0 International
