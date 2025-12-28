using System;
using System.Text;
using RimTalk.Client;
using RimTalk.Source.Data;
using UnityEngine;
using Verse;

namespace RimTalk.Data;

public class ApiLog(string name, TalkRequest talkRequest, string response, Payload payload, DateTime timestamp, Channel channel)
{
    
    public enum State
    {
        None, Pending, Ignored, Spoken, Failed
    }
    public Guid Id { get; } = Guid.NewGuid();
    public int ConversationId { get; set; }
    public TalkRequest TalkRequest { get; set; } = talkRequest ?? new TalkRequest(null, null);
    public string Name { get; set; } = name;
    public string Response { get; set; } = response;
    public string InteractionType;
    public bool IsFirstDialogue;
    public string RequestPayload { get; set; } = payload?.Request;
    public string ResponsePayload { get; set; } = payload?.Response;
    public int TokenCount { get; set; } = payload?.TokenCount ?? 0;
    public DateTime Timestamp { get; } = timestamp;
    public int ElapsedMs;
    public int SpokenTick { get; set; } = 0;
    public bool IsError { get; set; }
    public Channel Channel { get; set; } = channel;
    
    public State GetState()
    {
        if (IsError)
            return State.Failed;
        
        if (SpokenTick == -1 || Channel == Channel.Query)
            return State.Ignored;
        
        if (Response == null || SpokenTick == 0)
            return State.Pending;
        
        if (Response != null && SpokenTick > 0) 
            return State.Spoken;

        return State.None;
    }
    
    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Timestamp: {Timestamp:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Pawn: {Name ?? "-"}");
        sb.AppendLine($"InteractionType: {InteractionType ?? "-"}");
        sb.AppendLine($"ElapsedMs: {ElapsedMs}");
        sb.AppendLine($"TokenCount: {TokenCount}");
        sb.AppendLine($"SpokenTick: {SpokenTick}");
        sb.AppendLine();
        sb.AppendLine("=== Prompt ===");
        sb.AppendLine(TalkRequest.Prompt ?? string.Empty);
        sb.AppendLine();
        sb.AppendLine("=== Response ===");
        sb.AppendLine(Response ?? string.Empty);
        sb.AppendLine();
        sb.AppendLine("=== Contexts ===");
        sb.AppendLine(TalkRequest.Context);

        return sb.ToString().TrimEnd();
    }
}

public static class StateFilterExtensions
{
    public static string GetLabel(this ApiLog.State state)
    {
        return state switch
        {
            ApiLog.State.None => "RimTalk.DebugWindow.StateAll".Translate(),
            ApiLog.State.Pending => "RimTalk.DebugWindow.StatePending".Translate(),
            ApiLog.State.Ignored => "RimTalk.DebugWindow.StateIgnored".Translate(),
            ApiLog.State.Spoken => "RimTalk.DebugWindow.StateSpoken".Translate(),
            ApiLog.State.Failed => "RimTalk.DebugWindow.StateFailed".Translate(),
            _ => "Unknown"
        };
    }

    public static Color GetColor(this ApiLog.State state)
    {
        return state switch
        {
            ApiLog.State.Failed => ColorLibrary.RedReadable,
            ApiLog.State.Pending => Color.yellow,
            ApiLog.State.Ignored => Color.gray,
            ApiLog.State.Spoken => Color.green,
            _ => Color.white
        };
    }
}