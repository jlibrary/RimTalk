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

    protected override bool ReceiveData(byte[] data, int dataLength)
    {
        if (data == null || dataLength == 0) return false;

        string chunk = Encoding.UTF8.GetString(data, 0, dataLength);
        _buffer.Append(chunk);
        _allReceivedData.Append(chunk);

        string bufferContent = _buffer.ToString();
        string[] lines = bufferContent.Split(['\n'], StringSplitOptions.RemoveEmptyEntries);

        _buffer.Clear();
        if (!bufferContent.EndsWith("\n"))
        {
            _buffer.Append(lines[lines.Length - 1]);
        }

        int linesToProcess = bufferContent.EndsWith("\n") ? lines.Length : lines.Length - 1;
        for (int i = 0; i < linesToProcess; i++)
        {
            string line = lines[i].Trim();
            if (!line.StartsWith("data: ")) continue;
            string jsonData = line.Substring(6);

            if (jsonData.Trim() == "[DONE]") continue;

            try
            {
                var streamChunk = JsonUtil.DeserializeFromJson<Player2StreamChunk>(jsonData);
                
                if (!string.IsNullOrEmpty(streamChunk?.Id))
                {
                    _id = streamChunk.Id;
                }
                
                if (streamChunk?.Choices != null && streamChunk.Choices.Count > 0)
                {
                    var choice = streamChunk.Choices[0];
                    var content = choice?.Delta?.Content;
                    if (!string.IsNullOrEmpty(content))
                    {
                        _fullText.Append(content);
                        onContentReceived?.Invoke(content);
                    }

                    if (!string.IsNullOrEmpty(choice?.FinishReason))
                    {
                        _finishReason = choice.FinishReason;
                    }
                }

                if (streamChunk?.Usage != null)
                {
                    _totalTokens = streamChunk.Usage.TotalTokens;
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to parse Player2 stream chunk: {ex.Message}\nJSON: {jsonData}");
            }
        }
        return true;
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