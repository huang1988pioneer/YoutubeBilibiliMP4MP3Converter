using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace YoutubeOrBilibiliMP3Converter;

// Static artwork for the converter's input screen, drawn in a 272 × 132 viewbox.
internal sealed class MediaSignalDiagram(IBrush grid, IBrush video, IBrush audio, IBrush surface) : Control
{
    private static readonly Geometry Play = Geometry.Parse("M64,45 L64,71 L85,58 Z");
    private static readonly Geometry Trace = Geometry.Parse("M111,58 H138 L161,81 H183 M132,58 L149,41 H197");
    private static readonly int[] Wave = [5, 11, 21, 13, 29, 38, 18, 25, 11, 6];

    public override void Render(DrawingContext context)
    {
        var gridPen = new Pen(grid, 1);
        var videoPen = new Pen(video, 2);
        var audioPen = new Pen(audio, 2);
        for (var x = 8; x < 270; x += 16)
        {
            for (var y = 8; y < 132; y += 16)
            {
                context.DrawEllipse(grid, null, new Point(x, y), 1, 1);
            }
        }

        context.DrawGeometry(null, audioPen, Trace);
        context.DrawEllipse(surface, audioPen, new Point(197, 41), 3, 3);
        context.DrawRectangle(surface, gridPen, new Rect(20, 19, 87, 62), 6, 6);
        context.DrawRectangle(surface, videoPen, new Rect(27, 26, 87, 62), 6, 6);
        context.DrawLine(gridPen, new Point(28, 36), new Point(113, 36));
        context.DrawLine(gridPen, new Point(28, 78), new Point(113, 78));
        context.DrawGeometry(video, null, Play);
        context.DrawRectangle(surface, audioPen, new Rect(174, 55, 77, 52), 6, 6);
        for (var i = 0; i < Wave.Length; i++)
        {
            var x = 185 + i * 6;
            context.DrawLine(audioPen, new Point(x, 81 - Wave[i] * .5), new Point(x, 81 + Wave[i] * .5));
        }
    }
}
