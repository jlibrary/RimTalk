namespace RimTalk.Client;

public class Payload(string url, string model, string request, string response, int tokenCount, string errorMessage = null)
{
    public string URL { get; set; } = url;
    public string Model { get; set; } = model;
    public string Request { get; set; } = request;
    public string Response { get; set; } = response;
    public int TokenCount { get; set; } = tokenCount;
    public string ErrorMessage { get; set; } = errorMessage;
    
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