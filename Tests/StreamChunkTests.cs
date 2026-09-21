using RimTalk.Client.OpenAI;
using RimTalk.Util;
using Xunit;

namespace RimTalk.Tests;

public class StreamChunkTests
{
    [Theory]
    [InlineData("{\"choices\":[null]}")]
    [InlineData("{\"id\":\"gen-1\",\"choices\":[null],\"usage\":null}")]
    public void A_chunk_can_carry_a_null_choice(string json)
    {
        // Some providers emit this on a trailing or aborted chunk. The list is present
        // and non-empty, so a `Choices != null && Choices.Count > 0` guard lets it
        // through, and the element behind it is null. Anything reading Choices[0] has
        // to assume that.
        var chunk = JsonUtil.DeserializeFromJson<OpenAIStreamChunk>(json);

        Assert.NotNull(chunk.Choices);
        Assert.Single(chunk.Choices);
        Assert.Null(chunk.Choices[0]);
    }

    [Fact]
    public void An_empty_choice_object_is_not_a_null_choice()
    {
        // The distinction matters: `[{}]` gives a real object with null members, which
        // is safe to dereference. `[null]` does not.
        var chunk = JsonUtil.DeserializeFromJson<OpenAIStreamChunk>("{\"choices\":[{}]}");

        Assert.NotNull(chunk.Choices[0]);
        Assert.Null(chunk.Choices[0].FinishReason);
    }
}
