using RimTalk.Client;

namespace RimTalk.Error;

public class QuotaExceededException : AIRequestException
{
    public QuotaExceededException(string message, Payload payload = null) : base(message, payload)
    {
    }
}