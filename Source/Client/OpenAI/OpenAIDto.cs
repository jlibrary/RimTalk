using System.Collections.Generic;
using System.Runtime.Serialization;

namespace RimTalk.Client.OpenAI;
// === Request Models ===

[DataContract]
public class OpenAIRequest
{
    [DataMember(Name = "model")]
    public string Model { get; set; }

    [DataMember(Name = "messages")]
    public List<Message> Messages { get; set; } = [];

    [DataMember(Name = "temperature", EmitDefaultValue = false)]
    public double? Temperature { get; set; }

    [DataMember(Name = "max_tokens", EmitDefaultValue = false)]
    public int? MaxTokens { get; set; }

    [DataMember(Name = "top_p", EmitDefaultValue = false)]
    public double? TopP { get; set; }

    [DataMember(Name = "top_k", EmitDefaultValue = false)]
    public int? TopK { get; set; }

    [DataMember(Name = "frequency_penalty", EmitDefaultValue = false)]
    public double? FrequencyPenalty { get; set; }

    [DataMember(Name = "presence_penalty", EmitDefaultValue = false)]
    public double? PresencePenalty { get; set; }

    [DataMember(Name = "logit_bias", EmitDefaultValue = false)]
    public Dictionary<string, double> LogitBias { get; set; }

    [DataMember(Name = "stop", EmitDefaultValue = false)]
    public List<string> Stop { get; set; }

    [DataMember(Name = "stream", EmitDefaultValue = false)]
    public bool? Stream { get; set; }

    [DataMember(Name = "stream_options", EmitDefaultValue = false)]
    public StreamOptions StreamOptions { get; set; }
}

[DataContract]
public class StreamOptions
{
    [DataMember(Name = "include_usage", EmitDefaultValue = false)]
    public bool? IncludeUsage { get; set; }
}

[DataContract]
public class Message
{
    [DataMember(Name = "role")]
    public string Role { get; set; } // "system", "user", or "assistant"

    [DataMember(Name = "content")]
    public string Content { get; set; }
}

// === Response Models ===

[DataContract]
public class OpenAIResponse
{
    [DataMember(Name = "id")]
    public string Id { get; set; }

    [DataMember(Name = "object")]
    public string Object { get; set; }

    [DataMember(Name = "created")]
    public long Created { get; set; }

    [DataMember(Name = "model")]
    public string Model { get; set; }

    [DataMember(Name = "choices")]
    public List<Choice> Choices { get; set; }

    [DataMember(Name = "usage")]
    public Usage Usage { get; set; }
}

[DataContract]
public class Choice
{
    [DataMember(Name = "index")]
    public int Index { get; set; }

    [DataMember(Name = "message")]
    public Message Message { get; set; }

    [DataMember(Name = "finish_reason")]
    public string FinishReason { get; set; }
}

[DataContract]
public class Usage
{
    [DataMember(Name = "prompt_tokens")]
    public int PromptTokens { get; set; }

    [DataMember(Name = "completion_tokens")]
    public int CompletionTokens { get; set; }

    [DataMember(Name = "total_tokens")]
    public int TotalTokens { get; set; }
}

// === Stream Response Models ===

[DataContract]
public class OpenAIStreamChunk
{
    [DataMember(Name = "id")]
    public string Id { get; set; }

    [DataMember(Name = "object")]
    public string Object { get; set; }

    [DataMember(Name = "created")]
    public long Created { get; set; }

    [DataMember(Name = "model")]
    public string Model { get; set; }

    [DataMember(Name = "choices")]
    public List<StreamChoice> Choices { get; set; }

    [DataMember(Name = "usage")]
    public Usage Usage { get; set; }
}

[DataContract]
public class StreamChoice
{
    [DataMember(Name = "index")]
    public int Index { get; set; }

    [DataMember(Name = "delta")]
    public Delta Delta { get; set; }

    [DataMember(Name = "finish_reason")]
    public string FinishReason { get; set; }
}

[DataContract]
public class Delta
{
    [DataMember(Name = "content")]
    public string Content { get; set; }
}

[DataContract]
public class OpenAIModelsResponse
{
    [DataMember(Name = "data")]
    public List<Model> Data { get; set; }
}

[DataContract]
public class Model
{
    [DataMember(Name = "id")]
    public string Id { get; set; }
}