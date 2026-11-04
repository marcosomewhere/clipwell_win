using ClipwellWin.Services;
using Xunit;

namespace ClipwellWin.Tests;

public class OcrServiceTests
{
    [Fact]
    public void CalculateOcrScaledSize_KeepsSmallImagesUnchanged()
    {
        var size = OcrService.CalculateOcrScaledSize(1200, 800, 2600);

        Assert.Equal((1200u, 800u), size);
    }

    [Fact]
    public void CalculateOcrScaledSize_ScalesLongestSideToMaxDimension()
    {
        var size = OcrService.CalculateOcrScaledSize(3840, 2160, 2600);

        Assert.Equal(2600u, size.Width);
        Assert.Equal(1462u, size.Height);
    }
}
