using ResourceManager.Core;
using Xunit;

namespace ResourceManager.Core.Tests;

public class CoreTests
{
    [Theory]
    [InlineData(0, "0.0 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1073741824, "1.0 GB")]
    public void ByteSizeFormatsValues(long value, string expected) => Assert.Equal(expected, ByteSize.Format(value));

    [Fact]
    public async Task ThreatAnalyzerExplainsWritableUnsignedProcess()
    {
        var process = new ProcessSnapshot(new(42, DateTimeOffset.Now), "sample", @"C:\Users\Public\sample.exe", null,
            null, 2, 1024, 1024, 1, 1, null, "x64", null, false, DateTimeOffset.Now);
        var result = await new LocalThreatAnalyzer().AnalyzeAsync(process, []);
        Assert.True(result.Score >= 40);
        Assert.Contains(result.Evidence, e => e.Title.Contains("writable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CatalogDisclosesChangesForEveryComponent()
    {
        Assert.All(ComponentCatalog.CreateDefault(), component =>
        {
            Assert.NotEmpty(component.Capabilities);
            Assert.NotEmpty(component.SystemChanges);
            Assert.False(string.IsNullOrWhiteSpace(component.Publisher));
        });
    }
}
