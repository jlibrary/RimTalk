# OpenCode gateway session headers

RimTalk can talk to [opencode.ai](https://opencode.ai) (the **OpenCode Zen** and **OpenCode GO**
gateways). Those gateways identify a client session with the `x-opencode-session` request header and
reject chat-completion requests that do not carry it with `HTTP 400 {"type":"MissingSessionID"}`.
opencode's own client additionally sends the session-affinity family (`x-session-affinity`,
`x-client-request-id`, `x-session-id`) on its OpenAI-compatible branch.

RimTalk sent none of those headers, so an OpenCode endpoint could never answer a dialogue request.
This feature adds them, optionally.

## What it does

* Two provider entries are available in the provider dropdown: **OpenCode Zen**
  (`https://opencode.ai/zen/v1/chat/completions`) and **OpenCode GO**
  (`https://opencode.ai/zen/go/v1/chat/completions`), each with its matching `/models` URL.
* When the feature is enabled, every RimTalk request to `opencode.ai` (and any subdomain) carries:

  | Header | Value |
  |---|---|
  | `x-opencode-session` | derived session token |
  | `x-session-affinity` | same token |
  | `x-client-request-id` | same token |
  | `x-session-id` | same token |

* Both request paths are covered: `OpenAIClient.SendRequestAsync` (dialogue generation, streaming and
  non-streaming) and `OpenAIClient.FetchModelsAsync` (the **Verify / Fetch Models** button).
* A header explicitly configured elsewhere (for example through a provider definition) is never
  overwritten.
* **Only request headers change.** The body, URL, method, and every other option pass through
  unchanged, and requests to any other host are handed to Unity untouched — so the switch can stay on
  for OpenRouter, DeepSeek, a local Ollama server, and so on.

## Settings

**Options > Mod Settings > RimTalk > (Advanced API mode)**, at the bottom of the provider table:

* **OpenCode session headers** (`OpenCodeSessionHeaders`, default **on**) — the master switch. It is
  on by default because the OpenCode GO gateway requires the header; turn it off if you do not use an
  OpenCode endpoint.
* Next to the checkbox the UI shows the exact token that goes on the wire, so it can be matched
  against a gateway-side log.

The value is stored in the mod settings; requests are issued from thread-pool threads, so the request
path only ever reads it.

## How the token is derived

The wire token mirrors the dsh plugin
[gausszhou/dsh-opencode-session-id](https://github.com/gausszhou/dsh-opencode-session-id):

1. A stable per-installation seed (`rimtalk:<machine name>|<user name>`) is used as the session id.
2. SHA-256 hashes the seed, and the digest is walked 6 bits at a time into an alphanumeric
   `nanoid(8)` token (`A-Za-z0-9`, the shape of opencode's own web token), with rejection sampling so
   every character is uniform.
3. Only that token is sent: it is deterministic for the installation and one-way, so the gateway
   never sees the raw seed, and a machine that is renamed simply gets a new token.

`RimTalk.Client.OpenCodeSession` holds the pure logic (no Unity or Verse types) and
`RimTalk.Client.OpenCodeSessionInjector` is the thin Unity/`UnityWebRequest` glue. Host matching is
the only gate: requests to `opencode.ai` (or a subdomain) get the headers, everything else is handed
to Unity untouched. A user who configures a `Custom` provider against a different host therefore gets
no headers, which is also what keeps the feature from touching unrelated providers.

## Verification

```bash
# unit tests: host matching, header set, and nanoid parity against the reference implementation
dotnet test Tests/RimTalk.Tests.csproj --filter "FullyQualifiedName~OpenCode"
```

The expected tokens in `Tests/OpenCodeSessionTests.cs` were produced by running the reference
plugin's `nanoidOf()` under node, so the C# port is byte-for-byte compatible with it for every token
length the reference can emit (up to 42 characters). Past that point the reference throws
(`TypeError`, it assigns to a `const` after the first digest block); this port keeps hashing with a
counter instead, and a test pins the shared 42-character prefix.

The `opencode.ai` model lists used by the provider entries were checked live
(`GET https://opencode.ai/zen/v1/models` and `GET https://opencode.ai/zen/go/v1/models`, both
`200 OK`).

## Limitations

* Applies to the OpenAI-compatible HTTP paths RimTalk uses; a WebSocket transport, if one is ever
  added, would need its own injection point.
* All RimTalk dialogues on one machine share one session token. That is deliberate: the gateway uses
  the session for affinity/caching, and one stable token keeps a colony's prompts on one cache path.
* The gateway evaluates authentication before the session check, so `MissingSessionID` can only be
  reproduced end-to-end with a valid API key.
