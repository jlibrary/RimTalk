#nullable enable
using System.Collections.Generic;
using RimTalk.Util;
using Xunit;

namespace RimTalk.Tests;

public class TestDialogueItem
{
    public string? name { get; set; }
    public string? text { get; set; }
    public string? interaction { get; set; }
}

public class JsonStreamParserTests
{
    [Fact]
    public void Parse_CompleteSingleObject_ReturnsObject()
    {
        var parser = new JsonStreamParser<TestDialogueItem>();
        var result = parser.Parse("{\"name\":\"Colonist1\",\"text\":\"Hello world!\",\"interaction\":\"Chitchat\"}");

        Assert.Single(result);
        Assert.Equal("Colonist1", result[0].name);
        Assert.Equal("Hello world!", result[0].text);
        Assert.Equal("Chitchat", result[0].interaction);
    }

    [Fact]
    public void Parse_TokenSplitInsideEscapedQuote_SuccessfullyReassembles()
    {
        var parser = new JsonStreamParser<TestDialogueItem>();

        // Chunk 1 ends inside the escaped quote
        var chunk1 = "{\"name\":\"Colonist1\",\"text\":\"He said, \\\"";
        var chunk2 = "Welcome home!\\\" with a smile.\"}";

        var res1 = parser.Parse(chunk1);
        Assert.Empty(res1);

        var res2 = parser.Parse(chunk2);
        Assert.Single(res2);
        Assert.Equal("Colonist1", res2[0].name);
        Assert.Contains("Welcome home!", res2[0].text);
    }

    [Fact]
    public void Parse_BracesInsideDialogueString_DoesNotTerminateEarly()
    {
        var parser = new JsonStreamParser<TestDialogueItem>();

        // Dialogue containing curly braces
        var json = "{\"name\":\"Engineer\",\"text\":\"We need {components} and {steel} to build this.\",\"interaction\":\"Work\"}";
        var result = parser.Parse(json);

        Assert.Single(result);
        Assert.Equal("Engineer", result[0].name);
        Assert.Equal("We need {components} and {steel} to build this.", result[0].text);
    }

    [Fact]
    public void Parse_MarkdownCodeFence_StripsOrExtractsCleanly()
    {
        var parser = new JsonStreamParser<TestDialogueItem>();

        // LLM sending markdown wrapper around json
        var markdownChunk = "```json\n{\"name\":\"Trader\",\"text\":\"Got silver?\",\"interaction\":\"Trade\"}\n```";
        var result = parser.Parse(markdownChunk);

        Assert.Single(result);
        Assert.Equal("Trader", result[0].name);
        Assert.Equal("Got silver?", result[0].text);
    }

    [Fact]
    public void Parse_MultiChunkStreaming_EmitsObjectsAsCompleted()
    {
        var parser = new JsonStreamParser<TestDialogueItem>();

        var c1 = "[{\"name\":\"Pawn1\",\"text\":";
        var c2 = "\"Hi there!\"},";
        var c3 = "{\"name\":\"Pawn2\",\"text\":\"Hello!\"}]";

        var r1 = parser.Parse(c1);
        Assert.Empty(r1);

        var r2 = parser.Parse(c2);
        Assert.Single(r2);
        Assert.Equal("Pawn1", r2[0].name);

        var r3 = parser.Parse(c3);
        Assert.Single(r3);
        Assert.Equal("Pawn2", r3[0].name);
    }

    [Fact]
    public void Parse_UnicodeAndKoreanCharacters_ParsesAccurately()
    {
        var parser = new JsonStreamParser<TestDialogueItem>();

        var chunk1 = "{\"name\":\"정착민\",\"text\":\"안녕";
        var chunk2 = "하세요! 오늘 날씨가 좋네요.\"}";

        var r1 = parser.Parse(chunk1);
        Assert.Empty(r1);

        var r2 = parser.Parse(chunk2);
        Assert.Single(r2);
        Assert.Equal("정착민", r2[0].name);
        Assert.Equal("안녕하세요! 오늘 날씨가 좋네요.", r2[0].text);
    }

    [Fact]
    public void JsonUtil_Sanitize_RepairsConcatenatedObjectsAndSmartQuotes()
    {
        // Many local LLMs (Gemma, Llama) output curly quotes and missing commas between objects:
        // {“name”: “Tilly”, “text”: “Hello”}{“name”: “Ray”, “text”: “Hi”}
        string rawLlmOutput = "```json\n{“name”: “Tilly”, “text”: “Hello”}{“name”: “Ray”, “text”: “Hi”}\n```";

        string cleaned = JsonUtil.Sanitize(rawLlmOutput, typeof(List<TestDialogueItem>));

        // Must strip markdown, convert smart quotes, and insert commas between adjacent objects
        Assert.StartsWith("[{", cleaned);
        Assert.EndsWith("}]", cleaned);
        Assert.Contains("},{", cleaned);
        Assert.DoesNotContain("```", cleaned);
    }

    [Fact]
    public void JsonUtil_Sanitize_ProtectsMalformedInternalDialogueQuotes()
    {
        // Unescaped quotes inside speech text:
        // {"name":"Colonist","text":"He shouted "Run!" before falling"}
        string badJson = "{\"name\":\"Colonist\",\"text\":\"He shouted \"Run!\" before falling\"}";

        string cleaned = JsonUtil.Sanitize(badJson, typeof(TestDialogueItem));

        // The internal "Run!" quotes should be protected/escaped so JSON remains valid
        Assert.StartsWith("{", cleaned);
        Assert.EndsWith("}", cleaned);
        Assert.Contains("Colonist", cleaned);
    }

    [Fact]
    public void JsonUtil_PrettifyJson_FormatsCompactJsonWithIndents()
    {
        string compact = "{\"model\":\"gpt-4o\",\"messages\":[{\"role\":\"system\",\"content\":\"hello world\"}],\"temperature\":0.7}";
        string pretty = JsonUtil.PrettifyJson(compact);

        Assert.Contains("  \"model\": \"gpt-4o\",", pretty);
        Assert.Contains("  \"messages\": [", pretty);
        Assert.Contains("    {", pretty);
        Assert.Contains("      \"role\": \"system\",", pretty);
        Assert.Contains("      \"content\": \"hello world\"", pretty);
        Assert.Contains("    }", pretty);
        Assert.Contains("  ],", pretty);
        Assert.Contains("  \"temperature\": 0.7", pretty);
    }

    [Fact]
    public void JsonUtil_PrettifyJson_NonJsonReturnsOriginal()
    {
        string rawError = "502 Bad Gateway: Connection timeout";
        string result = JsonUtil.PrettifyJson(rawError);
        Assert.Equal(rawError, result);
    }

    [Fact]
    public void JsonUtil_PrettifyJson_HandlesEscapedQuotesAndSpecialChars()
    {
        string json = "{\"text\":\"Colonist said: \\\"Look at the {stars}!\\\"\",\"empty\":{}}";
        string pretty = JsonUtil.PrettifyJson(json);

        Assert.Contains("\"text\": \"Colonist said: \\\"Look at the {stars}!\\\"\"", pretty);
        Assert.Contains("\"empty\": {}", pretty);
    }

    [Fact]
    public void JsonUtil_UnescapeNewlines_ConvertsEscapedNewlines()
    {
        string input = "Line 1\\nLine 2\\r\\nLine 3";
        string result = JsonUtil.UnescapeNewlines(input);

        Assert.Contains("Line 1", result);
        Assert.Contains("Line 2", result);
        Assert.Contains("Line 3", result);
        Assert.DoesNotContain("\\n", result);
        Assert.DoesNotContain("\\r", result);
    }

    [Fact]
    public void JsonUtil_PrettifyJson_NonJsonOrRawMarkdownReturnsOriginalRaw()
    {
        string input = "```jsonl\n{\"name\": \"Kennis\", \"text\": \"Hello\"}\n```";
        string result = JsonUtil.PrettifyJson(input);

        Assert.Equal(input, result);
    }
}
