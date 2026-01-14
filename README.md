<p align="center">
  <a href="https://github.com/jlibrary/RimTalk/pulls">
    <img src="https://img.shields.io/badge/PRs-welcome-brightgreen.svg" alt="PRs Welcome">
  </a>
  <a href="https://github.com/jlibrary/RimTalk/blob/main/LICENSE">
    <img src="https://img.shields.io/badge/License-CC%20BY--NC--SA%204.0-lightgrey.svg" alt="license"/>
  </a>
  <a href="manifest.json">
    <img src="https://img.shields.io/badge/version-1.5,1.6-blue.svg" alt="version">
  </a>
  <a href="#">
    <img src="https://img.shields.io/badge/platform-steam-green.svg" alt="platform">
  </a>
</a>


<html>
    <h1 align="center">
      RimTalk 
    </h1>
    <h3 align="center">
      AI-Powered Dialogue MOD framework for RimWorld
    </h3>
</html>

> Latest Update: 01/16/2026

## 1. Overview

Bring your colonists to life with RimTalk, a revolutionary mod that uses AI to generate dynamic and context-aware dialogue for your pawns. Gone are the days of repetitive and generic interactions. With RimTalk, every conversation is unique, reflecting the pawn's current mood, thoughts, and situation.

RimTalk integrates directly with AI to generate dialogue in real-time. The mod analyzes a pawn's current thoughts, actions and many other information to create a prompt for the AI. The AI then generates a line of dialogue that is displayed in a bubble above the pawn's head. This process happens dynamically, creating emergent and immersive conversations that are unique to your colony's story.

## 2. Features

-   **AI-driven, context-rich dialogue:** Generates unique speech based on pawn mood, traits, relationships, and current situation.
-   **Modular prompt presets:** SillyTavern-style entries with role/position control, plus import/export and simple/advanced modes.
-   **Scriban templates:** Powerful C#-like logic, iteration (`for p in pawns`), conditionals, and full access to game objects (`pawn`, `map`, `Find`, etc.).
-   **Structured context control:** Presets and optimization to balance detail, token usage, and conversation history.
-   **Multi-provider support:** Google Gemini, OpenAI-compatible, custom Base URLs, and local providers with multiple configs.
-   **Flexible gameplay filters:** Player dialogue modes, monologue control, and faction/prisoner/slave/baby toggles.
-   **Extensible API:** Public API to register variables/appenders and inject prompt entries for other mods.

## 3. Installation & Quick Start

1.  **Subscribe** to the mod on the Steam Workshop.
2.  **Obtain a free Gemini API key:**
    *   Go to [Providers & Models](#6).
    *   Click on "Get an API key".
    *   Copy the generated API key.
3.  **Launch RimWorld** and enable the RimTalk mod in the mod list.
4.  **Open the mod settings** for RimTalk.
5.  **Paste your API key** into the designated field.

## 4. Configuration

Settings are organized into four tabs: Basic, Prompt Presets, Context Filter, and Event Filter.

### 4.1 Basic Settings

-   **API setup:** Simple mode (single key) or Advanced mode (multiple cloud configs, local provider, custom Base URL).
-   **Talk interval:** AI cooldown between pawn lines.
-   **Interaction handling:** Override non-RimTalk interactions and allow simultaneous conversations.
-   **Dialogue visibility:** Display speech while drafted and continue dialogue while sleeping.
-   **Gameplay toggles:** Allow monologues; allow slaves, prisoners, other factions, enemies, babies, and non-human pawns.
-   **Custom conversation:** Player dialogue mode (Disabled/Manual/AI-driven) and player name.
-   **Effects & performance:** Apply mood/social effects, disable AI at high game speed, and choose button display mode.

### 4.2 Prompt Presets (Simple/Advanced)

-   **Simple mode:** A single instruction field for quick tuning.
-   **Advanced mode:** Preset library with activate/duplicate/rename, entry enable/disable, and import/export.
-   **Entry controls:** Role (System/User/Assistant), position (Relative/InChat), and in-chat depth.
-   **Scriban helpers:** Variable insert menu and preview panel for built-in and runtime variables.

### 4.3 Context Filter

-   **Presets:** Essential, Standard, Comprehensive, and Custom.
-   **Optimization:** Toggle context optimization and set max pawn context count.
-   **History depth:** Configure conversation history lines.
-   **Pawn info:** Race/genes/ideology/backstory/traits/skills/health/mood/thoughts/relations/equipment/prisoner-slave status.
-   **Environment:** Time/date/season/weather/location/temperature/terrain/beauty/cleanliness/surroundings/wealth.

### 4.4 Event Filter

-   **Archivable types:** Choose which in-game messages/letters are included.
-   **Category grouping:** Letters, Messages, and Other are listed separately for quick filtering.
-   **Safe defaults:** `Verse.Message` is disabled by default; reset restores defaults.

## 5. Prompt System & Scriban Basics

RimTalk supports three levels of prompt setup, so you are not forced to use presets.

-   **Simple mode (plaintext system context):** A single instruction field; RimTalk appends context automatically.
-   **Simple preset (dialogue + context):** Minimal preset that concatenates `{{prompt}}` with `{{context}}`.
-   **Complex preset (advanced Scriban):** Full template control with logic, loops, and direct access to RimWorld objects.

Complex presets use **Scriban**, a powerful templating engine. You can use C#-style logic, access properties of pawns, iterate over lists, and even call utility methods.

-   **Core Objects:** Use `{{pawn}}` for the initiator, `{{recipient}}` for the target, and `{{pawns}}` for the list of participants.
-   **Property Access:** Access any public field or property (e.g., `{{pawn.LabelShort}}`, `{{pawn.gender}}`, `{{pawn.social}}` for relations summary, `{{map.weather}}`).
-   **Conditionals:** `{{ if recipient != null }}Talking to {{ recipient.LabelShort }}{{ else }}Monologue{{ end }}`.
-   **Loops:** `{{ for p in pawns }} * {{ p.LabelShort }} ({{ p.GetRole }}){{ end }}`.
-   **History:** `{{chat.history}}` inserts the conversation log formatted as `[Role] Message`.

Example JSON prompt template:

```json
{
  "system": "You are generating in-world dialogue for RimWorld. Keep it concise and immersive.",
  "player": "{{settings.PlayerName}}",
  "context": {
    "dialogue_type": "{{ctx.DialogueType}}",
    "dialogue_status": "{{ctx.DialogueStatus}}",
    "pawn": {
      "name": "{{pawn.name}}",
      "profile": "{{pawn.profile}}",
      "mood": "{{pawn.mood}}",
      "relations": "{{pawn.social}}"
    },
    "nearby_pawns": [
      {{ for p in pawns | array.offset 1 }}
      { "name": "{{p.name}}", "role": "{{p.role}}" }{{ if !for.last }},{{ end }}
      {{ end }}
    ],
    "environment": {
      "time": "{{hour}}h",
      "weather": "{{map.weather}}",
      "location": "{{pawn.location}}"
    }
  },
  "history": "{{chat.history}}",
  "user_prompt": "{{prompt}}"
}
```

## 6. Providers & Models

<a id="6"></a>

Only the hard-coded provider endpoints are listed here. Custom providers (Anthropic and other providers) are intentionally omitted.

| Provider | Endpoint | Models | Notes |
| --- | --- | --- | --- |
| Google Gemini | `https://generativelanguage.googleapis.com/v1beta` | `https://generativelanguage.googleapis.com/v1beta/models` |  |
| OpenAI | `https://api.openai.com/v1/chat/completions` | `https://api.openai.com/v1/models` |  |
| DeepSeek | `https://api.deepseek.com/v1/chat/completions` | `https://api.deepseek.com/models` |  |
| Grok (xAI) | `https://api.x.ai/v1/chat/completions` | `https://api.x.ai/v1/models` |  |
| GLM (zAI) | `https://api.z.ai/api/paas/v4/chat/completions` | `https://api.z.ai/api/paas/v4/models` |  |
| OpenRouter | `https://openrouter.ai/api/v1/chat/completions` | `https://openrouter.ai/api/v1/models` | Adds `HTTP-Referer` and `X-Title` headers |
| Qwen (Intl) | `https://dashscope-intl.aliyuncs.com/compatible-mode/v1/chat/completions` | `https://dashscope-intl.aliyuncs.com/compatible-mode/v1/models` |  |
| Qwen (CN) | `https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions` | `https://dashscope.aliyuncs.com/compatible-mode/v1/models` |  |
| Player2 | `https://api.player2.game` |  | Open your Player2 App while in game |

## 7. Troubleshooting

*   **No dialogue appearing:** Make sure you've entered a valid Gemini API key in the mod settings.
*   **Frequent pauses in dialogue:** You may be hitting rate limits. Consider increasing the Talk Interval setting.
*   **API key issues:** Ensure your API key is correctly copied from API platform without extra spaces.

We suggest that you post feedback on github issues, steam comments, Reddit or other platforms for us to solve problem.

## 8. How to Expand

Contribution workflow:
-   **Fork the repo** and create a feature branch.
-   **Make changes**, run in-game testing, and validate your prompt output.
-   **Open a PR** with a short summary and testing notes.

Common PR example: add a new `{{xxx}}` variable
1) Use `ContextHookRegistry.RegisterContextVariable` (or `RegisterPawnVariable`) in your mod's initialization.
2) If you want to modify existing template data, use `ContextHookRegistry.RegisterPawnHook`.
3) Variables are automatically available in Scriban templates.
4) (Optional) Add a translation entry in `VariableDefinitions.cs` to expose it in the variable list UI.
5) Test in-game with a prompt entry that includes `{{xxx}}`, confirm output and edge cases.

## 9. Compatibility & Notes

RimTalk is designed to be compatible with most other mods, but it does add a few Defs that can affect gameplay or UI:

-   **ThingDef:** `VocalLinkCatalyst` (a usable item in the Drugs category; grants the Vocal Link implant).
-   **HediffDef:** `VocalLinkImplant` (enables speech for non-human pawns) and `RimTalk_PersonaData` (hidden data store).
-   **ThoughtDef:** `RimTalk_Chitchat`, `RimTalk_KindWords`, `RimTalk_Slighted` (social thoughts that can affect mood/opinion when enabled).
-   **InteractionDef:** `RimTalkInteraction` (adds a social interaction entry and log text).
-   **JobDef:** `ApplyVocalLinkCatalyst` (job driver for using the catalyst).
-   **MainButtonDef:** `RimTalkDebug` (adds the overlay/debug launcher button).

If you are using mods that heavily rewrite social systems or interaction logs, check for conflicts with the thoughts/interactions above.

## 10. Opensource Expansions

- Feature Expansions
  - [RimTalk Expand Memory](https://github.com/sanguodxj-byte/RimTalk-ExpandMemory)
  - [RimTalk Expand Actions](https://github.com/sanguodxj-byte/RimTalk-ExpandActions)
  - [RimTalk Expand Literature](https://github.com/Alchuang22-dev/RimTalk-Literature-Expansion)
  - [RimTalk TTS](https://github.com/whatismyname0/RimTalkTTSModule)
- Context and Prompt
  - [RimTalk Prompt Enhance](https://github.com/a398276230-debug/rimtalk-prompt-enhance)
  - [RimTalk Event Plus](https://github.com/SaltGin/RimTalkEventPlus)
  - [RimTalk Persona Director](https://steamcommunity.com/sharedfiles/filedetails/?id=3619548407)
  - [RimTalk Quests](https://github.com/Laurence-042/RimTalk---Quests)

## 11. Credits

**Development:** Juicy

License under CC BY-NC-SA 4.0 International. 

