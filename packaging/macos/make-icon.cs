// Erzeugt AppIcon-1024.png (macOS-Symbolraster) aus rog-icon-square-1024.png – nur nötig, wenn sich die Vorlage ändert.
//   dotnet run packaging/macos/make-icon.cs -- packaging/macos/rog-icon-square-1024.png packaging/macos/AppIcon-1024.png
// Raster nach Apple (macOS 11+): 1024er Leinwand, Körper 824 × 824 mit 100 px Rand, Eckenradius 185, weicher Schatten.
// Die Vorlage wird nur verkleinert (1024 → 824), nie vergrößert.
#:property ManagePackageVersionsCentrally=false
#:property TreatWarningsAsErrors=false
#:package SkiaSharp@3.119.4
#:package SkiaSharp.NativeAssets.Linux@3.119.4
using SkiaSharp;

using var source = SKImage.FromEncodedData(args[0]);
using var surface = SKSurface.Create(new SKImageInfo(1024, 1024, SKColorType.Rgba8888, SKAlphaType.Premul));
var canvas = surface.Canvas;
canvas.Clear(SKColors.Transparent);
var body = new SKRect(100, 100, 924, 924);
using (var shadow = new SKPaint { Color = new SKColor(0, 0, 0, 80), IsAntialias = true, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 14) })
{
    canvas.DrawRoundRect(new SKRect(100, 112, 924, 936), 185, 185, shadow);
}

canvas.Save();
using (var clip = new SKPath())
{
    clip.AddRoundRect(body, 185, 185);
    canvas.ClipPath(clip, antialias: true);
}

canvas.DrawImage(source, body, new SKSamplingOptions(SKCubicResampler.Mitchell));
canvas.Restore();
using (var edge = new SKPaint { Color = new SKColor(0x3A, 0x40, 0x50), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3 })
{
    canvas.DrawRoundRect(new SKRect(101.5f, 101.5f, 922.5f, 922.5f), 183.5f, 183.5f, edge);
}

using var png = surface.Snapshot().Encode(SKEncodedImageFormat.Png, 100);
File.WriteAllBytes(args[1], png.ToArray());
