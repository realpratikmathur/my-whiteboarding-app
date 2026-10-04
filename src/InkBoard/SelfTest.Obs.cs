using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using InkBoard.Core;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Input.Inking;

namespace InkBoard;

// Self-test scenario "obs-chart": a test card for choosing the private-notes colour. The left half holds
// every regular pen colour, highlighter, pencil and text; the right half holds one row per candidate
// private colour (argument: comma-separated RRGGBB list). The pixel rectangle of every block is written to
// obs-chart.json so recordings of this window can be measured.
public sealed partial class MainWindow
{
    private static Color FromHex(string rrggbb)
    {
        uint value = Convert.ToUInt32(rrggbb, 16);
        return Color.FromArgb(0xFF, (byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }

    private int Squiggle(InkDrawingAttributes attributes, double x, double y, double length, double amplitude = 7)
    {
        var points = new List<InkPoint>();
        for (int i = 0; i <= 50; i++)
        {
            double t = i / 50.0;
            points.Add(new InkPoint(new Point(x + length * t, y + amplitude * Math.Sin(t * 18)), 0.35f + 0.4f * (float)t));
        }

        var builder = new InkStrokeBuilder();
        builder.SetDefaultDrawingAttributes(attributes);
        return _document.AddNew(builder.CreateStrokeFromInkPoints(points, Matrix3x2.Identity));
    }

    /// <summary>
    /// Scenario "obs-final": the test card with the real private colour, written with the app's own LUT file
    /// next to it (obs-lut.png), then a recorded lesson left paused in replay so a capture shows regular ink
    /// replaying while the private notes sit on the page.
    /// </summary>
    internal async Task RunObsFinalAsync(StringBuilder log)
    {
        string folder = Path.GetDirectoryName(SelfTest.LogPath)!;
        await PrivateInk.SaveObsLutAsync(Path.Combine(folder, "obs-lut.png"));
        await RunObsChartAsync(log, PrivateInk.Hex);

        // Private notes written with the private pen's own attributes, and a private text box.
        StartRecording();
        // Exactly what the toolbar does when the private pen is selected (its palette holds one colour).
        InkDrawingAttributes secret = new PrivatePen().CreateInkDrawingAttributes(
            new Microsoft.UI.Xaml.Media.SolidColorBrush(PrivateInk.Color), 4);
        InkDrawingAttributes otherBrush = new PrivatePen().CreateInkDrawingAttributes(
            new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black), 4);
        log.AppendLine($"private pen colour #{secret.Color.R:X2}{secret.Color.G:X2}{secret.Color.B:X2} (expect {PrivateInk.Hex}); " +
            $"with a black brush #{otherBrush.Color.R:X2}{otherBrush.Color.G:X2}{otherBrush.Color.B:X2}");
        _undo.Push(new AddStrokesStep([Squiggle(secret, 740, 200, 400)]) { Drawn = true });
        AddObjectWithUndo(new PageObject
        {
            Kind = PageObjectKind.Text, Text = "Say: now the blue line", X = 740, Y = 240, Color = PrivateInk.Hex,
        });
        InkDrawingAttributes pen = Presenter.CopyDefaultDrawingAttributes();
        pen.Color = InkPalettes.Pens[16];
        pen.Size = new Size(5, 5);
        await DrawAsync(pen, new Point(740, 330), new Point(1300, 330), 900);
        await DrawAsync(pen, new Point(740, 400), new Point(1300, 400), 900);
        StopRecording();
        await SaveCurrentPageAsync();
        log.AppendLine($"private items in the recording: {_recording.Events.Count(e => e.Object is not null && PrivateInk.IsPrivate(e.Object))} objects; " +
            $"recorded strokes {_recordedStrokeCount}, of which private {_recordedStrokes.GetStrokes().Count(PrivateInk.IsPrivate)}");

        // Replay, paused half-way through the first blue line.
        await EnterReplayAsync();
        int privateOnPage = _document.Container.GetStrokes().Count(PrivateInk.IsPrivate);
        SetReplayPlaying(true);
        double pauseAt = _timeline.Items.Last(i => i.Event.Duration > 0).Start + 450;   // half-way through the second blue line
        while (!_clock.AtEnd && _clock.Position < pauseAt)
        {
            await Task.Delay(20);
        }

        SetReplayPlaying(false);
        log.AppendLine($"replay paused at {_clock.Position:0} ms: private strokes on page {privateOnPage} -> " +
            $"{_document.Container.GetStrokes().Count(PrivateInk.IsPrivate)}, private text boxes {_objects.Count(PrivateInk.IsPrivate)}, " +
            $"regular strokes {_document.Container.GetStrokes().Count(s => !PrivateInk.IsPrivate(s))}");
        await Task.Delay(500);
    }

    internal async Task RunObsChartAsync(StringBuilder log, string candidateList)
    {
        string[] candidates = candidateList.Split(',', StringSplitOptions.RemoveEmptyEntries);
        PagesToggle.IsChecked = false;
        await Task.Delay(300);

        // ---- Regular content -------------------------------------------------------------------
        const double RowHeight = 26;
        for (int i = 0; i < InkPalettes.Pens.Count; i++)
        {
            InkDrawingAttributes pen = Presenter.CopyDefaultDrawingAttributes();
            pen.Color = InkPalettes.Pens[i];
            pen.Size = new Size(i % 2 == 0 ? 4 : 2, i % 2 == 0 ? 4 : 2);
            Squiggle(pen, 40, 40 + i * RowHeight, 280);
        }

        for (int i = 0; i < InkPalettes.Highlighters.Count; i++)
        {
            var highlighter = new InkDrawingAttributes
            {
                DrawAsHighlighter = true, PenTip = PenTipShape.Rectangle, Size = new Size(6, 20),
                Color = InkPalettes.Highlighters[i], IgnorePressure = true,
            };
            Squiggle(highlighter, 360, 46 + i * 40, 300, amplitude: 3);
        }

        Color[] pencils = [InkPalettes.Pens[0], InkPalettes.Pens[5], InkPalettes.Pens[16], InkPalettes.Pens[12]];
        for (int i = 0; i < pencils.Length; i++)
        {
            InkDrawingAttributes pencil = InkDrawingAttributes.CreateForPencil();
            pencil.Color = pencils[i];
            pencil.Size = new Size(8, 8);
            Squiggle(pencil, 360, 420 + i * 34, 300);
        }

        ShowObject(new PageObject { Kind = PageObjectKind.Text, Text = "Regular text 0123 AaBbCc", X = 360, Y = 560 });
        var regular = new Rect(30, 20, 660, 600);

        // ---- Candidate private colours -----------------------------------------------------------
        const double CandidateHeight = 60;
        var rows = new List<(string Hex, Rect Rect)>();
        for (int i = 0; i < candidates.Length; i++)
        {
            double top = 24 + i * (CandidateHeight + 2);
            InkDrawingAttributes pen = Presenter.CopyDefaultDrawingAttributes();
            pen.Color = FromHex(candidates[i]);
            pen.Size = new Size(4, 4);
            Squiggle(pen, 740, top + 16, 250);
            pen.Size = new Size(2, 2);
            Squiggle(pen, 740, top + 42, 250);
            ShowObject(new PageObject
            {
                Kind = PageObjectKind.Text, Text = "Private note 0123 AaBb", X = 1020, Y = top + 10, Color = candidates[i],
            });
            rows.Add((candidates[i], new Rect(730, top, 600, CandidateHeight)));
        }

        CanvasScroller.ChangeView(0, 0, 1f, disableAnimation: true);
        Root.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
        await Task.Delay(1200);

        // ---- Where everything is, in window client pixels ----------------------------------------
        double scale = Root.XamlRoot.RasterizationScale;
        Point origin = PageSurface.TransformToVisual(Root).TransformPoint(new Point(0, 0));
        object Pixels(Rect r) => new
        {
            x = (int)Math.Round((origin.X + r.X) * scale), y = (int)Math.Round((origin.Y + r.Y) * scale),
            w = (int)Math.Round(r.Width * scale), h = (int)Math.Round(r.Height * scale),
        };
        string json = JsonSerializer.Serialize(new
        {
            scale,
            regular = Pixels(regular),
            candidates = rows.Select(r => new { hex = r.Hex, rect = Pixels(r.Rect) }),
        });
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(SelfTest.LogPath)!, "obs-chart.json"), json);
        log.AppendLine($"chart: {InkPalettes.Pens.Count} pens, {InkPalettes.Highlighters.Count} highlighters, {candidates.Length} candidates; scale {scale}; page origin {origin}");
    }
}
