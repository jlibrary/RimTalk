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

        // 4. Try robust parsing via SimpleJsonParser { "error": "message" } or { "detail": "message" }
        try
        {
            var parsed = JsonUtil.ParseJsonValue(jsonResponse.Trim(), out _);
            if (parsed is Dictionary<string, object> dict)
            {
                if (dict.TryGetValue("error", out var errVal))
                {
                    if (errVal is string errStr && !string.IsNullOrEmpty(errStr)) return errStr;
                    if (errVal is Dictionary<string, object> errObj && errObj.TryGetValue("message", out var mVal) && mVal is string mStr)
                    {
                        return mStr;
                    }
                }
                if (dict.TryGetValue("detail", out var detailVal) && detailVal is string detailStr && !string.IsNullOrEmpty(detailStr))
                {
                    return detailStr;
                }
                if (dict.TryGetValue("message", out var msgVal) && msgVal is string msgStr && !string.IsNullOrEmpty(msgStr))
                {
                    return msgStr;
                }
            }
        }
        catch
        {
            // Ignore JSON parsing exceptions and return null
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
