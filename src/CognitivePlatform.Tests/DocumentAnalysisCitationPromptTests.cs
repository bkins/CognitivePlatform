using System.Security.Cryptography;
using System.Text;
using CognitivePlatform.Api.DocumentAnalysis;

namespace CognitivePlatform.Tests;

public sealed class DocumentAnalysisCitationPromptTests
{
    private static DocumentSnapshot Source(string key, string text)
        => new(key, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))), text);

    [Fact]
    public void Prompt_Labels_Exact_Original_Lines_And_Explicit_Request_Owned_Ids()
    {
        var source = Source("buffer-7-Wind.md", "# Wind\r\nMoving air\r\nBattery storage");
        var prompt = new DocumentAnalysisCitationPrompt([source]);

        var text = prompt.Build("Compare generation; exclude the recipe.");

        Assert.Contains("\"sourceId\":\"S1\"", text);
        Assert.Contains("buffer-7-Wind.md", text);
        Assert.Contains("\"line\":2", text);
        Assert.Contains("Moving air", text);
        Assert.Contains("exclude the recipe", text);
        Assert.Contains("not use excluded sources", text);
    }

    [Fact]
    public void Valid_Assigned_Ids_Resolve_To_Original_Keys_Without_Trusting_Model_Order()
    {
        var prompt = new DocumentAnalysisCitationPrompt([Source("solar-key", "Sun\nBatteries"), Source("wind-key", "Air\nBatteries")]);

        var text = prompt.NormalizeCitations("Wind [[documents|S2|L1-L2]]; sun [[S1|L1]].");

        Assert.Equal("Wind [[wind-key|L1-L2]]; sun [[solar-key|L1]].", text);
    }

    [Theory]
    [InlineData("[[S3|L1]]")]
    [InlineData("[[S1|L0]]")]
    [InlineData("[[S1|L3]]")]
    [InlineData("[[S1|L2-L1]]")]
    [InlineData("[[other|S1|L1]]")]
    [InlineData("[[documents|S1|L1-L3]]")]
    public void Unknown_Or_Out_Of_Range_Tokens_Remain_Unverified(string token)
    {
        var prompt = new DocumentAnalysisCitationPrompt([Source("source-key", "One\nTwo")]);

        Assert.Equal(token, prompt.NormalizeCitations(token));
    }

    [Fact]
    public void Changed_Source_Hash_Cannot_Be_Normalized()
    {
        var source = Source("source-key", "Original");
        var prompt = new DocumentAnalysisCitationPrompt([source with { Text = "Changed" }]);

        Assert.Equal("[[S1|L1]]", prompt.NormalizeCitations("[[S1|L1]]"));
    }
}
