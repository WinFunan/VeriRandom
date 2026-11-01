using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.Logging;
using QRCoder;

namespace SecRandom.Services.SlideTrigger;

/// <summary>
///     Renders a courseware marker as a QR image and puts it where the teacher can paste it.
/// </summary>
/// <remarks>
///     <para>
///         The intended flow is one key press: the marker lands on the clipboard, the teacher switches to their deck,
///         pastes, and drags it to size. Nothing here writes into a deck — a courseware file is the teacher's own
///         document, and an app that edits it is an app that can corrupt it.
///     </para>
///     <para>
///         The image is rendered black-on-white with QRCoder's built-in four-module quiet zone. That is not cosmetic:
///         a marker pasted onto a dark slide, or cropped to its modules, stops decoding, and the failure mode is
///         "nothing happens on that slide" — the hardest kind of problem for a teacher to diagnose.
///     </para>
/// </remarks>
public sealed class SlideTriggerMarkerImageService(ILogger<SlideTriggerMarkerImageService> logger)
{
    /// <summary>
    ///     Module size in pixels. A marker is roughly 33–41 modules across, so this lands it around 300–400 px: big
    ///     enough to paste comfortably and small enough that PowerPoint's default resize keeps it sharp.
    /// </summary>
    private const int PixelsPerModule = 10;

    private const int SuggestedPixelSize = 400;

    /// <summary>Renders the marker as a PNG. Kept separate from the clipboard so it can be saved or previewed.</summary>
    public byte[] RenderPng(string markerText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(markerText);

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(markerText, QRCodeGenerator.ECCLevel.M);

        // ECC M tolerates roughly 15% damage — the level to pick for a code that will be projected, photographed by
        // accident, and possibly re-encoded by a screen-sharing pipeline.
        return new PngByteQRCode(data).GetGraphic(PixelsPerModule);
    }

    /// <summary>
    ///     Renders the marker and puts it on the clipboard, which is the whole point of the flow: press, switch to the
    ///     deck, paste.
    /// </summary>
    /// <returns>False when the clipboard is unavailable, so the caller can offer the file fallback instead of
    /// pretending it copied.</returns>
    public async Task<bool> TryCopyToClipboardAsync(TopLevel? topLevel, string markerText)
    {
        if (topLevel?.Clipboard is not { } clipboard)
            return false;

        try
        {
            // QRCoder's PngByteQRCode never touches GDI, so this path works on every desktop target.
            using var stream = new MemoryStream(RenderPng(markerText));
            using var bitmap = new Bitmap(stream);

            await clipboard.SetBitmapAsync(bitmap).ConfigureAwait(true);

            // Windows/X11 hand the clipboard out lazily: without a flush, pasting after the app exits yields nothing.
            // That is exactly the case here — the teacher presses the key, then leaves the app for the deck.
            try
            {
                await clipboard.FlushAsync().ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                logger.LogDebug(exception, "刷新剪贴板失败，图片可能无法在应用退出后粘贴");
            }

            return true;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "把课件标记放入剪贴板失败");
            return false;
        }
    }

    /// <summary>
    ///     Fallback for a host without a working clipboard, and the way to hand a marker to another machine. Visible
    ///     text arrives from the caller because a service holds no localization of its own.
    /// </summary>
    public async Task<string?> ExportAsync(
        TopLevel? topLevel,
        string markerText,
        string suggestedFileName,
        string pickerTitle,
        string fileTypeName)
    {
        if (topLevel?.StorageProvider is not { } storage)
            return null;

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = pickerTitle,
            SuggestedFileName = EnsurePngExtension(suggestedFileName),
            DefaultExtension = "png",
            FileTypeChoices = [new FilePickerFileType(fileTypeName) { Patterns = ["*.png"] }]
        });

        if (file is null)
            return null;

        try
        {
            var png = RenderPng(markerText);

            await using var target = await file.OpenWriteAsync().ConfigureAwait(true);
            await target.WriteAsync(png).ConfigureAwait(true);

            return file.TryGetLocalPath() ?? file.Name;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "导出课件标记失败");
            return null;
        }
    }

    /// <summary>Pixels the pasted image will occupy before the teacher resizes it, for the caller's hint text.</summary>
    public static int SuggestedSizePx => SuggestedPixelSize;

    private static string EnsurePngExtension(string fileName) =>
        fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? fileName : fileName + ".png";
}
