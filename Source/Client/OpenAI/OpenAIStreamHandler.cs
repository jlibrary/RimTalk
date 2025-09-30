using System;
using System.Text;
using RimTalk.Util;
using UnityEngine.Networking;

namespace RimTalk.Client.OpenAI;

/// <summary>
/// A custom download handler that processes Server-Sent Events (SSE) streams for OpenAI.
/// </summary>
public class OpenAIStreamHandler : DownloadHandlerScript
{
    private readonly Action<string> _onContentReceived;
    private readonly StringBuilder _buffer = new();
    private readonly StringBuilder _fullText = new();
    private readonly StringBuilder _rawJson = new();
    private int _totalTokens;

    public OpenAIStreamHandler(Action<string> onContentReceived)
    {
        _onContentReceived = onContentReceived;
    }

    public int GetTotalTokens() => _totalTokens;

    public string GetRawJson() => _rawJson.ToString();

    protected override bool ReceiveData(byte[] data, int dataLength)
    {
        if (data == null || dataLength == 0) return false;

        _buffer.Append(Encoding.UTF8.GetString(data, 0, dataLength));
        string bufferContent = _buffer.ToString();
        string[] lines = bufferContent.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

        _buffer.Clear();
        if (!bufferContent.EndsWith("\n"))
        {
            _buffer.Append(lines[lines.Length - 1]);
        }

        int linesToProcess = bufferContent.EndsWith("\n") ? lines.Length : lines.Length - 1;
        for (int i = 0; i < linesToProcess; i++)
        {
            string line = lines[i].Trim();
            if (line.StartsWith("data: "))
            {
                string jsonData = line.Substring(6);
                _rawJson.Append(jsonData);

                if (jsonData.Trim() == "[DONE]")
                {
                    continue;
                }

                try
                {
                    var openAIChunk = JsonUtil.DeserializeFromJson<OpenAIStreamChunk>(jsonData);

                    if (openAIChunk?.Choices != null && openAIChunk.Choices.Count > 0)
                    {
                        var content = openAIChunk.Choices[0]?.Delta?.Content;
                        if (!string.IsNullOrEmpty(content))
                        {
                            _fullText.Append(content);
                            _onContentReceived?.Invoke(content);
                        }
                    }

                    if (openAIChunk?.Usage != null)
                    {
                        _totalTokens = openAIChunk.Usage.TotalTokens;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Failed to parse stream chunk: {ex.Message}\nJSON: {jsonData}");
                }
            }
        }
        return true;
    }

    public string GetFullText() => _fullText.ToString();
}