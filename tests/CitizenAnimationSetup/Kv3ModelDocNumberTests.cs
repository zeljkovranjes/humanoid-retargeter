using HumanoidRetargeter.Target;
using Xunit;

namespace HumanoidRetargeter.Tests;

public class Kv3ModelDocNumberTests
{
    [Theory]
    [InlineData(1.0380962e-05)]
    [InlineData(-1.2e-12)]
    [InlineData(1.25e25)]
    [InlineData(double.Epsilon)]
    [InlineData(double.MaxValue)]
    public void ModelDocNumbersHaveNoExponentAndRoundTripExactly(double value)
    {
        var doc = Kv3.Parse(VmdlWriter.Kv3Header + "\n{ value = 0.0 }");
        ((KvObject)doc.Root)["value"] = new KvDouble(value);
        var text = Kv3.Serialize(doc);
        var number = text[(text.IndexOf("value = ", StringComparison.Ordinal) + 8)..].Trim().TrimEnd('}').Trim();
        Assert.DoesNotContain("E", number);
        Assert.DoesNotContain("e", number);
        Assert.Equal(value, ((KvDouble)((KvObject)Kv3.Parse(text).Root)["value"]).Value);
    }
}
