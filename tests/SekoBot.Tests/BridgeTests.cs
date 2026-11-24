using SekoBot.Bot;
using SekoBot.Memory;
using Xunit;

namespace SekoBot.Tests;

public class BridgeTests
{
    [Fact]
    public void Roads_on_both_banks_join_over_the_brown_bridge()
    {
        // A grey road coming down, blue water across, a brown bridge, the road going on below.
        int w = 60, h = 90;
        var px = new (byte R, byte G, byte B)[w * h];
        for (int i = 0; i < px.Length; i++) px[i] = (55, 62, 30);                       // ground
        for (int y = 38; y < 52; y++) for (int x = 0; x < w; x++) px[y * w + x] = (57, 77, 99);   // water
        for (int y = 0; y < 38; y++) for (int x = 29; x < 31; x++) px[y * w + x] = (86, 83, 86);  // road
        for (int y = 52; y < h; y++) for (int x = 29; x < 31; x++) px[y * w + x] = (86, 83, 86);
        for (int y = 38; y < 52; y++) for (int x = 28; x < 32; x++) px[y * w + x] = (88, 74, 56); // bridge
        var road = RoadMap.RoadPixels(px, w, h);
        for (int y = 5; y < h - 5; y++)
            Assert.True(road[y * w + 29] || road[y * w + 30] || road[y * w + 28] || road[y * w + 31], $"gap at {y}");
    }

    [Fact]
    public void A_short_road_ending_nowhere_is_cut_off_the_long_road_stays()
    {
        int w = 120, h = 60;
        var px = new (byte R, byte G, byte B)[w * h];
        for (int i = 0; i < px.Length; i++) px[i] = (55, 62, 30);
        for (int x = 5; x < 115; x++) for (int y = 20; y < 22; y++) px[y * w + x] = (86, 83, 86);   // the road
        for (int y = 22; y < 37; y++) for (int x = 60; x < 62; x++) px[y * w + x] = (86, 83, 86);   // a 15-px stub
        var road = RoadMap.RoadPixels(px, w, h);
        Assert.True(road[20 * w + 30] && road[20 * w + 90]);
        Assert.False(road[33 * w + 60] || road[33 * w + 61], "the stub stays");
    }
}
