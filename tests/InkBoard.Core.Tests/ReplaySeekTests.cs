using InkBoard.Core;

namespace InkBoard.Core.Tests;

public class ReplaySeekTests
{
    [Fact]
    public void Seek_jumps_forward_and_back()
    {
        var clock = new ReplayClock(10_000);

        clock.Seek(7_500);
        Assert.Equal(7_500, clock.Position);

        clock.Seek(1_200);
        Assert.Equal(1_200, clock.Position);
    }

    [Fact]
    public void Seek_stays_inside_the_recording()
    {
        var clock = new ReplayClock(10_000);

        clock.Seek(-500);
        Assert.Equal(0, clock.Position);

        clock.Seek(99_999);
        Assert.Equal(10_000, clock.Position);
        Assert.True(clock.AtEnd);
    }

    [Fact]
    public void Seek_keeps_playing_or_paused()
    {
        var playing = new ReplayClock(10_000) { IsPlaying = true };
        playing.Seek(3_000);
        Assert.True(playing.IsPlaying);
        playing.Advance(1_000);
        Assert.Equal(4_000, playing.Position);

        var paused = new ReplayClock(10_000);
        paused.Seek(3_000);
        Assert.False(paused.IsPlaying);
        paused.Advance(1_000);
        Assert.Equal(3_000, paused.Position);
    }

    [Fact]
    public void Seeking_to_the_end_stops_playback()
    {
        var clock = new ReplayClock(10_000) { IsPlaying = true };

        clock.Seek(10_000);

        Assert.False(clock.IsPlaying);
    }
}
