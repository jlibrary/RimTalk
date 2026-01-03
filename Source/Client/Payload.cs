namespace RimTalk.Client;

public class Payload(string url, string model, string request, string response, int tokenCount, string errorMessage = null)
{
    public readonly string URL = url;
    public readonly string Model = model;
    public readonly string Request = request;
    public readonly string Response = response;
    public readonly int TokenCount = tokenCount;
    public string ErrorMessage = errorMessage;
    
    public override string ToString()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== RIMTALK API REPORT ===");
        sb.AppendLine($"URL:      {URL}");
        sb.AppendLine($"Model:    {Model}");
        sb.AppendLine($"Tokens:   {TokenCount}");
        if (!string.IsNullOrEmpty(ErrorMessage))
            sb.AppendLine($"Error:    {ErrorMessage}");
        sb.AppendLine();
        sb.AppendLine("--- REQUEST PAYLOAD ---");
        sb.AppendLine(Request ?? "EMPTY");
        sb.AppendLine();
        sb.AppendLine("--- RESPONSE PAYLOAD ---");
        sb.AppendLine(Response ?? "EMPTY");
        sb.AppendLine("==========================");
    
        return sb.ToString();
    }
}