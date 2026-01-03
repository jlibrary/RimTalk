using System;
using RimTalk.Client;

namespace RimTalk.Error;

public class AIRequestException : Exception
{
    public Payload Payload { get; }

    public AIRequestException(string message, Payload payload) : base(message)
    {
        Payload = payload;
    }
}