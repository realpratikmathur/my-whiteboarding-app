using InkBoard.Core;

namespace InkBoard.Core.Tests;

public class TextStyleTests
{
    [Fact]
    public void New_text_defaults_to_segoe_ui_20_regular()
    {
        var text = new PageObject { Kind = PageObjectKind.Text };

        Assert.Equal(("Segoe UI", 20, false, false), (text.FontFamily, text.FontSize, text.Bold, text.Italic));
    }

    [Fact]
    public void Style_can_be_copied_and_compared_without_touching_text_or_position()
    {
        var source = new PageObject { FontFamily = "Georgia", FontSize = 36, Bold = true, Italic = true };
        var target = new PageObject { Text = "Hello", X = 10, Y = 20 };
        Assert.False(target.HasSameStyleAs(source));

        target.CopyStyleFrom(source);

        Assert.True(target.HasSameStyleAs(source));
        Assert.Equal(("Hello", 10, 20), (target.Text, target.X, target.Y));
    }

    [Fact]
    public void Style_survives_json()
    {
        var text = new PageObject { Kind = PageObjectKind.Text, FontFamily = "Consolas", FontSize = 10.5, Bold = true };

        PageObject loaded = System.Text.Json.JsonSerializer.Deserialize<PageObject>(System.Text.Json.JsonSerializer.Serialize(text))!;

        Assert.True(loaded.HasSameStyleAs(text));
    }

    [Fact]
    public void Pages_saved_before_fonts_existed_load_with_the_default_font()
    {
        PageObject loaded = System.Text.Json.JsonSerializer.Deserialize<PageObject>("""{"Kind":0,"Text":"old","FontSize":24}""")!;

        Assert.Equal(("Segoe UI", 24, false, false), (loaded.FontFamily, loaded.FontSize, loaded.Bold, loaded.Italic));
    }

    [Fact]
    public void A_recorded_style_change_is_part_of_the_recording_state()
    {
        var id = Guid.NewGuid();
        var recording = new Recording
        {
            Events =
            [
                new RecordedEvent { Kind = RecordedEventKind.ObjectAdd, ObjectId = id, Time = 100, Object = new PageObject { Id = id, Text = "Title" } },
                new RecordedEvent
                {
                    Kind = RecordedEventKind.ObjectStyle, ObjectId = id, Time = 200,
                    Object = new PageObject { Id = id, FontFamily = "Georgia", FontSize = 48, Bold = true },
                },
            ],
        };

        PageObject state = RecordingState.AtEnd(recording).Objects[id];

        Assert.Equal(("Georgia", 48, true, "Title"), (state.FontFamily, state.FontSize, state.Bold, state.Text));
    }

    [Fact]
    public void Existing_recordings_keep_their_event_numbers()
    {
        // Events are stored by number; a new kind must be added at the end.
        Assert.Equal(6, (int)RecordedEventKind.ObjectText);
        Assert.Equal(7, (int)RecordedEventKind.ObjectStyle);
    }
}
