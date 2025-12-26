using System.Collections.Generic;
using System.Runtime.Serialization;

namespace RimTalk.Client.Gemini;

[DataContract]
public class GeminiDto
{
    [DataMember(Name = "system_instruction")]
    public SystemInstruction SystemInstruction { get; set; }

    [DataMember(Name = "contents")]
    public List<Content> Contents { get; set; } = [];

    [DataMember(Name = "generationConfig")]
    public GenerationConfig GenerationConfig { get; set; }

    [DataMember(Name = "safetySettings")]
    public List<SafetySetting> SafetySettings { get; set; }
}

[DataContract]
public class SystemInstruction
{
    [DataMember(Name = "parts")]
    public List<Part> Parts { get; set; } = [];
}

[DataContract]
public class Content
{
    [DataMember(Name = "role")]
    public string Role { get; set; } = "user";

    [DataMember(Name = "parts")]
    public List<Part> Parts { get; set; } = [];
}

[DataContract]
public class Part
{
    [DataMember(Name = "text")]
    public string Text { get; set; } = string.Empty;
}

[DataContract]
public class GenerationConfig
{
    [DataMember(Name = "temperature")]
    public float Temperature { get; set; } = 0.7f;

    [DataMember(Name = "topK")]
    public int TopK { get; set; } = 40;

    [DataMember(Name = "topP")]
    public float TopP { get; set; } = 0.95f;

    [DataMember(Name = "maxOutputTokens")]
    public int MaxOutputTokens { get; set; } = 8192;

    [DataMember(Name = "thinkingConfig")]
    public ThinkingConfig ThinkingConfig { get; set; }
}

[DataContract]
public class ThinkingConfig
{
    [DataMember(Name = "thinkingBudget")]
    public int ThinkingBudget { get; set; }
}

[DataContract]
public class SafetySetting
{
    [DataMember(Name = "category")]
    public string Category { get; set; } = string.Empty;

    [DataMember(Name = "threshold")]
    public string Threshold { get; set; } = string.Empty;
}

// Response Models
[DataContract]
public class GeminiResponse
{
    [DataMember(Name = "candidates")]
    public List<Candidate> Candidates { get; set; } = [];

    [DataMember(Name = "usageMetadata")]
    public UsageMetadata UsageMetadata { get; set; }
}

[DataContract]
public class Candidate
{
    [DataMember(Name = "content")]
    public Content Content { get; set; } = new Content();

    [DataMember(Name = "finishReason")]
    public string FinishReason { get; set; } = string.Empty;

    [DataMember(Name = "index")]
    public int Index { get; set; }
}

[DataContract]
public class UsageMetadata
{
    [DataMember(Name = "promptTokenCount")]
    public int PromptTokenCount { get; set; }

    [DataMember(Name = "candidatesTokenCount")]
    public int CandidatesTokenCount { get; set; }

    [DataMember(Name = "totalTokenCount")]
    public int TotalTokenCount { get; set; }
}

[DataContract]
public class GoogleModelsResponse
{
    [DataMember(Name = "models")]
    public List<GoogleModelData> Models;
}

[DataContract]
public class GoogleModelData
{
    [DataMember(Name = "name")]
    public string Name;
    [DataMember(Name = "displayName")]
    public string DisplayName;
    [DataMember(Name = "description")]
    public string Description;
    [DataMember(Name = "supportedGenerationMethods")]
    public List<string> SupportedGenerationMethods;
}