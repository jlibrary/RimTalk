using System;
using System.Text;
using RimTalk.Util;
using UnityEngine.Networking;

namespace RimTalk.Client.Player2;

public class Player2StreamHandler(Action<string> onContentReceived) : DownloadHandlerScript
{
    private readonly StringBuilder _buffer = new();
    private readonly StringBuilder _fullText = new();
    private readonly StringBuilder _allReceivedData = new();
    private int _totalTokens;
    private string _id;
    private string _finishReason;

    public string DetectedError { get; private set; }

    protected override bool ReceiveData(byte[] data, int dataLength)
    {
        if (data == null || dataLength == 0) return false;

        string chunk = Encoding.UTF8.GetString(data, 0, dataLength);
        _allReceivedData.Append(chunk);
        _buffer.Append(chunk);

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
            ProcessLine(lines[i].Trim());
        }
        return true;
    }

    public void Flush()
    {
        if (_buffer.Length > 0)
        {
            ProcessLine(_buffer.ToString().Trim());
            _buffer.Clear();
        }
    }

    private void ProcessLine(string line)
    {
        if (!line.StartsWith("data: ")) return;

        string jsonData = line.Substring(6).Trim();
        if (jsonData == "[DONE]") return;

        try
        {
            var chunk = JsonUtil.DeserializeFromJson<Player2StreamChunk>(jsonData);

            // 1. Check for Error immediately
            if (!string.IsNullOrEmpty(chunk?.Error))
            {
                DetectedError = chunk.Error;
                return;
            }

            // 2. Process Content
            if (!string.IsNullOrEmpty(chunk?.Id)) _id = chunk.Id;
            
            if (chunk?.Choices != null && chunk.Choices.Count > 0)
            {
                var choice = chunk.Choices[0];
                if (!string.IsNullOrEmpty(choice?.Delta?.Content))
                {
                    _fullText.Append(choice.Delta.Content);
                    onContentReceived?.Invoke(choice.Delta.Content);
                }
                if (!string.IsNullOrEmpty(choice?.FinishReason)) _finishReason = choice.FinishReason;
            }

            if (chunk?.Usage != null) _totalTokens = chunk.Usage.TotalTokens;
        }
        catch (Exception ex)
        {
            Logger.Warning($"Stream parse error: {ex.Message}\nJSON: {jsonData}");
        }
    }

    public string GetFullText() => _fullText.ToString();
    public int GetTotalTokens() => _totalTokens;
    public string GetAllReceivedText() => _allReceivedData.ToString();

    public string GetRawJson()
    {
        var response = new Player2Response
        {
            Id = _id,
            Choices =
            [
                new Choice
                {
                    Message = new Message
                    {
                        Role = "assistant",
                        Content = GetFullText()
                    },
                    FinishReason = _finishReason
                }
            ],
            Usage = new Usage
            {
                TotalTokens = _totalTokens
            }
        };

        return JsonUtil.SerializeToJson(response);
    }
}