namespace RimTalk.Client;

public class Payload(string request, string response, int tokenCount)
{
    public string Request = request;
    public string Response = response;
    public int TokenCount = tokenCount;
}