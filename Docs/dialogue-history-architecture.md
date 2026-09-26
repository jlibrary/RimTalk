# Dialogue History Architecture & Decision Log

## 1. Background & Approaches

RimTalk needs to pass past dialogue to the LLM so pawns remember conversations without blowing up token costs or repeating themselves.

Three approaches were used during development:

1. **Approach A (Native Multi-turn / `history_raw`)**:
   * Uses alternating `user` and `assistant` message arrays.
   * Matches standard chat templates (`<|im_start|>assistant` etc.).
2. **Approach B (Dialogue Only - Intermediate Attempt)**:
   * Attempted to minimize token usage by stripping all past triggers and grouping only spoken dialogue (`Name: Text`) into a single text block.
3. **Approach B' (Single Block with Triggers / `history` - Current Default)**:
   * Groups dialogue into a single text block, but keeps a short trigger line (`prompt: ...`) before each dialogue turn.

---

## 2. Practical Trade-offs

| Approach | Pros | Cons | Best Fit |
|---|---|---|---|
| **Approach A** (Native Multi-turn) | • Follows native chat role structure.<br>• Works much better on small/local models (Llama-3-8B) that struggle to separate instructions from dialogue within a single block.<br>• Zero chance of outputting `prompt:` markers. | • Uses more tokens due to repeated message wrappers and triggers per call. | Small or local models, deep 1:1 interactions. |
| **Approach B** (Dialogue Only) | • Lowest token usage. | • Pawns repeated previous lines in long monologues.<br>• In arguments or fights, pawns forgot who started the conflict and reset their tone. | Abandoned due to testing issues. |
| **Approach B'** (Single Block + Triggers) | • Saves tokens compared to Approach A.<br>• Keeping `prompt: ...` fixes the tone reset in conflicts and breaks up repetitive text blocks. | • Weaker role separation than native multi-turn.<br>• Small models may occasionally copy the `prompt:` format into output. | Frontier cloud models (GPT-4o, Claude 3.5, Gemini). |

---

## 3. Issues Observed in Testing

### 1. Repetition in Long Monologues
* **What happened in Approach B**: When a single pawn talked to themselves over multiple turns, the history became a plain list of `Name: Text`. The model quickly started repeating almost the exact same lines with minor word variations.
* **Why B' fixes it**: Adding `prompt: [thought/topic]` before each line introduces varied wording into the context, breaking the repetitive pattern.

### 2. Losing Cause-and-Effect in Conflicts
* **What happened in Approach B**: If Pawn B insulted Pawn A, and A snapped back, the next turn only showed A's angry retort without B's initial insult. Pawn B would respond with *"Why are you so angry?"* or act friendly, resetting the tension.
* **Why A and B' fix it**: Retaining the trigger (`prompt: Pawn B insulted Pawn A`) lets the model understand why A was hostile and continue the argument naturally.

### 3. Small Model Formatting Errors
* On small models (e.g., Llama-3-8B), packing `[Chat History]`, `prompt: ...`, and `Name: Text` into a single user message often confused the model. It would sometimes output `prompt:` in its actual dialogue response.
* Approach A completely avoids this because past lines sit in native `assistant` slots.

### 4. Ambient Environment Leakage
* Prompts frequently contain temporary surroundings like `Nearby: Dead raider x1`, `Room: Dirt floor`.
* If left in history, pawns kept complaining about dead bodies hours later even after the corpse was buried and they were eating in a clean room.
* **Fix**: Ambient data (`Nearby`, `Room`, `Wealth`) and formatting instructions are stripped before saving a trigger to history. Only the actual event or topic is kept.

---

## 4. Current Decision

* **Default to Approach B' (`{{chat.history}}`)**: Keeps token usage low on cloud models while preserving enough context to avoid repetition loops and tone resets.
* **Keep Approach A as opt-in (`{{chat.history_raw}}`)**: Available for users running local/small models or those who prefer standard multi-turn formatting.
