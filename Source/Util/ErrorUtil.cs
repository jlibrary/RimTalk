using System.Collections.Generic;
using RimTalk.Client.OpenAI;

namespace RimTalk.Util;

public static class ErrorUtil
{
    public static string ExtractErrorMessage(string jsonResponse)
    {
        if (string.IsNullOrEmpty(jsonResponse)) return null;

        // 1. Try standard wrapped ErrorResponse { "error": { ... } }
        if (JsonUtil.TryDeserializeFromJson<ErrorResponse>(jsonResponse, out var wrapped, out _))
        {
            if (wrapped?.Error != null) return FormatError(wrapped.Error);
        }

        // 2. Try flat ErrorDetail { "message": "...", "code": ... }
        if (JsonUtil.TryDeserializeFromJson<ErrorDetail>(jsonResponse, out var flat, out _))
        {
            if (!string.IsNullOrEmpty(flat.Message)) return FormatError(flat);
        }

        // 3. Try array-wrapped [ { "error": ... } ]
        if (JsonUtil.TryDeserializeFromJson<List<ErrorResponse>>(jsonResponse, out var list, out _))
        {
            if (list != null && list.Count > 0 && list[0].Error != null)
            {
                return FormatError(list[0].Error);
            }
        }

        return null;
    }

    private static string FormatError(ErrorDetail detail)
    {
        if (detail == null) return null;

        string msg = detail.Message;
        if (string.IsNullOrEmpty(msg)) msg = detail.Status;
        if (string.IsNullOrEmpty(msg)) msg = detail.Type;
        if (string.IsNullOrEmpty(msg)) return null;

        if (detail.Code != 0)
        {
            return $"[{detail.Code}] {msg}";
        }

        if (!string.IsNullOrEmpty(detail.Status) && detail.Status != msg)
        {
            return $"({detail.Status}) {msg}";
        }

        return msg;
    }
}
