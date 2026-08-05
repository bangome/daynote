using System.Buffers.Binary;
using System.Diagnostics;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Daynote.App.Showcase;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests;

[TestClass]
public sealed class ShowcaseCaptureTests
{
    [TestMethod]
    public async Task CaptureCli_WritesValidPngAndConsistentMetadata()
    {
        var output = Path.Combine(Path.GetTempPath(), $"daynote-capture-{Guid.NewGuid():N}");
        Directory.CreateDirectory(output);
        try
        {
            var executable = Path.Combine(
                Path.GetDirectoryName(typeof(ShowcaseManifest).Assembly.Location)!,
                "Daynote.App.exe");
            Assert.IsTrue(File.Exists(executable), $"Missing showcase executable: {executable}");

            var startInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var argument in new[]
            {
                "--showcase", "--page", "wide.app-shell.default", "--output", output,
                "--width", "101.25", "--height", "53.25", "--scale", "2",
                "--palette", "standard", "--motion", "reduced", "--frame", "settled",
                "--stress", "long"
            })
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo)!;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                Assert.Fail("The showcase capture did not exit within 20 seconds.");
            }

            var standardOutput = await process.StandardOutput.ReadToEndAsync();
            var standardError = await process.StandardError.ReadToEndAsync();
            Assert.AreEqual(0, process.ExitCode, $"stdout:{Environment.NewLine}{standardOutput}{Environment.NewLine}stderr:{Environment.NewLine}{standardError}");

            var pngPath = Directory.GetFiles(output, "*.png").Single();
            var metadataPath = Directory.GetFiles(output, "*.json")
                .Single(path => !path.EndsWith("showcase-manifest.json", StringComparison.Ordinal));
            var png = await File.ReadAllBytesAsync(pngPath);
            CollectionAssert.AreEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png[..8]);
            Assert.AreEqual(203, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)));
            Assert.AreEqual(107, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));
            using (var imageStream = File.OpenRead(pngPath))
            {
                var frame = new PngBitmapDecoder(
                    imageStream,
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad).Frames[0];
                var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
                var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
                converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
                Assert.IsGreaterThanOrEqualTo(
                    (byte)254,
                    pixels[^1],
                    "A 200% capture must paint through the bottom-right physical pixel.");
            }

            var metadata = JsonSerializer.Deserialize<ShowcaseCaptureMetadata>(
                await File.ReadAllTextAsync(metadataPath), ShowcaseManifest.JsonOptions)!;
            Assert.AreEqual("wide.app-shell.default", metadata.PageId);
            Assert.AreEqual(Path.GetFileName(pngPath), metadata.Png);
            StringAssert.Contains(metadata.BuildIdentity, "+mvid.");
            Assert.IsGreaterThan(DateTimeOffset.MinValue, metadata.SourceModifiedUtc);
            Assert.AreEqual(101.25, metadata.WidthDip);
            Assert.AreEqual(53.25, metadata.HeightDip);
            Assert.AreEqual(2, metadata.Scale);
            Assert.AreEqual(203, metadata.PixelWidth);
            Assert.AreEqual(107, metadata.PixelHeight);
            Assert.AreEqual(ShowcasePalette.Standard, metadata.Palette);
            Assert.AreEqual(ShowcaseMotion.Reduced, metadata.Motion);
            Assert.AreEqual(ShowcaseFrame.Settled, metadata.Frame);
            Assert.AreEqual(ShowcaseStress.Long, metadata.Stress);
            Assert.AreEqual("default", metadata.State);
            Assert.AreEqual("none", metadata.ActualFocusedAutomationName);
            StringAssert.Contains(metadata.InputPath, "no pointer or keyboard input executed");
            Assert.IsFalse(metadata.InputPath.Contains("paths exposed", StringComparison.Ordinal));
            Assert.IsFalse(string.IsNullOrWhiteSpace(metadata.ActualScrollOwnerAutomationName));
            Assert.IsNotEmpty(metadata.UiaState);

            var runManifest = JsonSerializer.Deserialize<ShowcaseRunManifest>(
                await File.ReadAllTextAsync(Path.Combine(output, "showcase-manifest.json")),
                ShowcaseManifest.JsonOptions)!;
            Assert.AreEqual("daynote.showcase/v1", runManifest.Manifest.Schema);
            Assert.HasCount(1, runManifest.Captures);
            Assert.AreEqual(metadata.Png, runManifest.Captures[0].Png);
        }
        finally
        {
            Directory.Delete(output, recursive: true);
        }
    }
}
