using System.Drawing;
using SekoBot.UI;
using Xunit;

namespace SekoBot.Tests;

public class WindowFitTests
{
    [Fact]
    public void FitsWholeWhenTheScreenIsBigEnough() =>
        Assert.Equal(new Size(991, 773), MainForm.FitSize(new Size(991, 773), new Size(1920, 1040), 17, 17));

    [Fact]
    public void ShorterScreenCutsTheHeightAndMakesRoomForTheScrollBar() =>
        // 1366×768 laptop: the bottom scrolls, the width grows by the scroll bar (no sideways scrolling).
        Assert.Equal(new Size(1008, 728), MainForm.FitSize(new Size(991, 773), new Size(1366, 728), 17, 17));

    [Fact]
    public void NarrowAndShortScreenCutsBoth() =>
        Assert.Equal(new Size(1000, 700), MainForm.FitSize(new Size(1239, 966), new Size(1000, 700), 21, 21));
}
