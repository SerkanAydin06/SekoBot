using SekoBot.UI;
using Xunit;

namespace SekoBot.Tests;

public class TelegramScreenshotTests
{
    [Theory]
    [InlineData(640, 480, 640, 480)]
    [InlineData(3840, 2160, 2560, 1440)]
    public void Screenshot_is_a_JPEG_with_bounded_dimensions(int w, int h, int expectedW, int expectedH)
    {
        using var image = new Bitmap(w, h);
        using var bytes = new MemoryStream(MainForm.ScreenshotJpeg(image));
        using var decoded = new Bitmap(bytes);
        Assert.Equal(System.Drawing.Imaging.ImageFormat.Jpeg.Guid, decoded.RawFormat.Guid);
        Assert.Equal(new Size(expectedW, expectedH), decoded.Size);
    }
}
