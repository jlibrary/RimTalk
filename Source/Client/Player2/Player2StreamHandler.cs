using System;
using System.Text;
using RimTalk.Util;
using UnityEngine.Networking;

namespace RimTalk.Client.Player2;

public class Player2StreamHandler(Action<string> onContentReceived) : DownloadHandlerScript
{
    private readonly StringBuilder _buffer = new();
    private readonly StringBuilder _fullText = new();
    private int _totalTokens;
    private string _id;
    private string _finishReason;

    protected override bool ReceiveData(byte[] data, int dataLength)
    {
        if (data == null || dataLength == 0) return false;

        _buffer.Append(Encoding.UTF8.GetString(data, 0, dataLength));
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
                var chunk = JsonUtil.DeserializeFromJson<Player2StreamChunk>(jsonData);
                
                if (!string.IsNullOrEmpty(chunk?.Id))
                {
                    _id = chunk.Id;
                }
                
                if (chunk?.Choices != null && chunk.Choices.Count > 0)
                {
                    var choice = chunk.Choices[0];
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

                if (chunk?.Usage != null)
                {
                    _totalTokens = chunk.Usage.TotalTokens;
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
}