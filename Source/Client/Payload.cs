namespace RimTalk.Client
{
    public class Payload
    {
        public string Request;
        public string Response;
        public int TokenCount;

        public Payload(string request, string response, int tokenCount)
        {
            Request = request;
            Response = response;
            TokenCount = tokenCount;
        }
    }
}