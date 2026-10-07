using Xunit;
using HumanoidRetargeter.Core;

namespace HumanoidRetargeter.Tests;

public class SmokeTest
{
    [Fact]
    public void DevAssemblyCompilesAndIsReferenced()
    {
        Assert.Equal("0.1.0", CoreInfo.Version);
    }
}
