using System;
using System.Text;
using RimTalk.Util;
using UnityEngine.Networking;

namespace RimTalk.Client.Gemini;

/// <summary>
/// A custom download handler that processes Server-Sent Events (SSE) streams for Gemini.
/// </summary>
public class GeminiStreamHandler : DownloadHandlerScript
{
    private readonly Action<string> _onJsonReceived;
    private readonly StringBuilder _buffer = new();
    private readonly StringBuilder _fullText = new();
    private readonly StringBuilder _rawJson = new();
    private int _totalTokens;

    public GeminiStreamHandler(Action<string> onJsonReceived)
    {
        _onJsonReceived = onJsonReceived;
    }

    protected override bool ReceiveData(byte[] data, int dataLength)
    {
        if (data == null || dataLength == 0) return false;

        string chunk = Encoding.UTF8.GetString(data, 0, dataLength);
        _buffer.Append(chunk);

        ProcessBuffer();
        return true;
    }

    private void ProcessBuffer()
    {
        string bufferContent = _buffer.ToString();
        string[] lines = bufferContent.Split(new[] { '\n' }, StringSplitOptions.None);

        _buffer.Clear();
        _buffer.Append(lines[lines.Length - 1]);

        for (int i = 0; i < lines.Length - 1; i++)
        {
            string line = lines[i].Trim();
            if (line.StartsWith("data: "))
            {
                string jsonData = line.Substring(6);
                _rawJson.Append(jsonData);
                ProcessStreamChunk(jsonData);
            }
        }
    }

    private void ProcessStreamChunk(string jsonData)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(jsonData)) return;

            var response = JsonUtil.DeserializeFromJson<GeminiResponse>(jsonData);

            if (response?.Candidates is { Count: > 0 } && response.Candidates[0]?.Content?.Parts is { Count: > 0 })
            {
                string content = response.Candidates[0].Content.Parts[0].Text;
                if (!string.IsNullOrEmpty(content))
                {
                    _fullText.Append(content);
                    _onJsonReceived?.Invoke(content);
                }
            }

            if (response?.UsageMetadata?.TotalTokenCount > 0)
            {
                _totalTokens = response.UsageMetadata.TotalTokenCount;
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to parse streaming chunk: {ex.Message}\nJSON: {jsonData}");
        }
    }

    public string GetFullText() => _fullText.ToString();
    public int GetTotalTokens() => _totalTokens;
    public string GetRawJson() => _rawJson.ToString();
}