using SkiaSharp;

// Renders the app icon as vector art at any size, then writes PNG / iconset / ICO.
var outDir = args.Length > 0 ? args[0] : "out";
var mode = args.Length > 1 ? args[1] : "macos"; // macos: padded squircle, full: full-bleed
Directory.CreateDirectory(outDir);

var brandStops = new[] { SKColor.Parse("#FF3D5A"), SKColor.Parse("#FB7299"), SKColor.Parse("#23ADE5") };

void Draw(SKCanvas c, bool padded)
{
    c.Clear(SKColors.Transparent);
    var inset = padded ? 100f : 0f;
    var tile = new SKRect(inset, inset, 1024 - inset, 1024 - inset);
    var radius = tile.Width * 0.225f;

    // Tile with soft drop shadow
    using (var shadow = new SKPaint
    {
        IsAntialias = true,
        Color = new SKColor(0, 0, 0, 70),
        ImageFilter = SKImageFilter.CreateBlur(18, 18)
    })
    {
        if (padded)
        {
            var s = tile;
            s.Offset(0, 14);
            c.DrawRoundRect(s, radius, radius, shadow);
        }
    }

    using (var bg = new SKPaint
    {
        IsAntialias = true,
        Shader = SKShader.CreateLinearGradient(
            new SKPoint(tile.Left, tile.Top), new SKPoint(tile.Right, tile.Bottom),
            brandStops, new[] { 0f, 0.5f, 1f }, SKShaderTileMode.Clamp)
    })
    {
        c.DrawRoundRect(tile, radius, radius, bg);
    }

    // Glossy top light
    using (var gloss = new SKPaint
    {
        IsAntialias = true,
        Shader = SKShader.CreateLinearGradient(
            new SKPoint(0, tile.Top), new SKPoint(0, tile.MidY),
            new[] { new SKColor(255, 255, 255, 70), new SKColor(255, 255, 255, 0) },
            null, SKShaderTileMode.Clamp)
    })
    {
        c.DrawRoundRect(tile, radius, radius, gloss);
    }

    // Everything below is laid out on the 824px tile and scaled for full-bleed mode.
    c.Save();
    var scale = tile.Width / 824f;
    c.Translate(tile.Left, tile.Top);
    c.Scale(scale);

    var white = new SKPaint { IsAntialias = true, Color = SKColors.White };
    var soft = new SKPaint
    {
        IsAntialias = true,
        Color = new SKColor(20, 20, 60, 60),
        ImageFilter = SKImageFilter.CreateBlur(14, 14)
    };

    // Bilibili-style TV: antennas + screen
    var antenna = new SKPaint
    {
        IsAntialias = true,
        Color = SKColors.White,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 40,
        StrokeCap = SKStrokeCap.Round
    };
    c.DrawLine(300, 232, 236, 150, antenna);
    c.DrawLine(524, 232, 588, 150, antenna);

    var screen = new SKRect(128, 222, 696, 612);
    var shadowRect = screen;
    shadowRect.Offset(0, 16);
    c.DrawRoundRect(shadowRect, 96, 96, soft);
    c.DrawRoundRect(screen, 96, 96, white);

    // Play triangle in brand gradient, rounded corners
    using (var tri = new SKPath())
    {
        tri.MoveTo(338, 318);
        tri.LineTo(338, 516);
        tri.LineTo(512, 417);
        tri.Close();
        using var triPaint = new SKPaint
        {
            IsAntialias = true,
            PathEffect = SKPathEffect.CreateCorner(34),
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(338, 318), new SKPoint(512, 516),
                new[] { SKColor.Parse("#FF3D5A"), SKColor.Parse("#F0569A") }, null, SKShaderTileMode.Clamp)
        };
        c.DrawPath(tri, triPaint);
    }

    // Green MP3 badge with a music note
    var badgeCenter = new SKPoint(624, 628);
    c.DrawCircle(badgeCenter.X, badgeCenter.Y + 12, 150, soft);
    c.DrawCircle(badgeCenter, 150, white);
    using (var green = new SKPaint
    {
        IsAntialias = true,
        Shader = SKShader.CreateLinearGradient(
            new SKPoint(badgeCenter.X - 124, badgeCenter.Y - 124), new SKPoint(badgeCenter.X + 124, badgeCenter.Y + 124),
            new[] { SKColor.Parse("#34D399"), SKColor.Parse("#16A34A") }, null, SKShaderTileMode.Clamp)
    })
    {
        c.DrawCircle(badgeCenter, 126, green);
    }

    var stem = new SKPaint
    {
        IsAntialias = true,
        Color = SKColors.White,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 26,
        StrokeCap = SKStrokeCap.Round,
        StrokeJoin = SKStrokeJoin.Round
    };
    // two beamed eighth notes
    c.DrawLine(598, 672, 598, 566, stem);
    c.DrawLine(690, 648, 690, 542, stem);
    var beam = new SKPaint { IsAntialias = true, Color = SKColors.White, Style = SKPaintStyle.Fill };
    using (var beamPath = new SKPath())
    {
        beamPath.MoveTo(585, 552);
        beamPath.LineTo(703, 522);
        beamPath.LineTo(703, 556);
        beamPath.LineTo(585, 586);
        beamPath.Close();
        c.DrawPath(beamPath, beam);
    }
    void NoteHead(float x, float y)
    {
        c.Save();
        c.RotateDegrees(-22, x, y);
        c.DrawOval(new SKRect(x - 40, y - 29, x + 40, y + 29), white);
        c.Restore();
    }
    NoteHead(566, 680);
    NoteHead(658, 656);

    c.Restore();
}

SKBitmap Render(int size, bool padded)
{
    var info = new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
    using var surface = SKSurface.Create(info);
    surface.Canvas.Scale(size / 1024f);
    Draw(surface.Canvas, padded);
    return SKBitmap.FromImage(surface.Snapshot());
}

byte[] Png(SKBitmap bmp) => bmp.Encode(SKEncodedImageFormat.Png, 100).ToArray();

if (mode == "preview")
{
    File.WriteAllBytes(Path.Combine(outDir, "preview-1024.png"), Png(Render(1024, true)));
    foreach (var s in new[] { 128, 64, 32, 16 })
    {
        File.WriteAllBytes(Path.Combine(outDir, $"preview-{s}.png"), Png(Render(s, true)));
    }
    return;
}

// macOS iconset (padded squircle per macOS grid)
var iconset = Path.Combine(outDir, "AppIcon.iconset");
Directory.CreateDirectory(iconset);
foreach (var (name, size) in new[]
{
    ("icon_16x16", 16), ("icon_16x16@2x", 32), ("icon_32x32", 32), ("icon_32x32@2x", 64),
    ("icon_128x128", 128), ("icon_128x128@2x", 256), ("icon_256x256", 256), ("icon_256x256@2x", 512),
    ("icon_512x512", 512), ("icon_512x512@2x", 1024)
})
{
    File.WriteAllBytes(Path.Combine(iconset, name + ".png"), Png(Render(size, true)));
}

// In-app / Windows use the full-bleed tile (no transparent margin)
File.WriteAllBytes(Path.Combine(outDir, "app-icon.png"), Png(Render(1024, false)));

var icoSizes = new[] { 16, 24, 32, 48, 64, 128, 256 };
var images = icoSizes.Select(s => Png(Render(s, false))).ToArray();
using (var fs = File.Create(Path.Combine(outDir, "app.ico")))
using (var w = new BinaryWriter(fs))
{
    w.Write((ushort)0);
    w.Write((ushort)1);
    w.Write((ushort)icoSizes.Length);
    var offset = 6 + 16 * icoSizes.Length;
    for (var i = 0; i < icoSizes.Length; i++)
    {
        var s = icoSizes[i];
        w.Write((byte)(s >= 256 ? 0 : s));
        w.Write((byte)(s >= 256 ? 0 : s));
        w.Write((byte)0);
        w.Write((byte)0);
        w.Write((ushort)1);
        w.Write((ushort)32);
        w.Write(images[i].Length);
        w.Write(offset);
        offset += images[i].Length;
    }
    foreach (var img in images)
    {
        w.Write(img);
    }
}

Console.WriteLine("done");
