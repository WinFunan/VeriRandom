using System;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Services.SlideTrigger;
using SecRandom.Platforms.Abstractions;
using ZXing;
using ZXing.Common;

namespace SecRandom.Services.SlideTrigger;

/// <summary>
///     Turns one captured frame into a marker, or into "nothing here".
/// </summary>
/// <remarks>
///     <para>
///         Screen-to-screen decoding is the easy case: the frame is a 1:1 copy of the framebuffer, so there is no
///         perspective, no motion blur and no camera noise — the usual reasons a QR code fails. What is left is DPI
///         scaling and a marker that was resized too small on the slide, which is why the settings page exposes this as
///         a one-shot "check the current screen" action: it answers "does it work on my projector" in two seconds.
///     </para>
///     <para>
///         Only QR is scanned. Restricting the format is not a micro-optimisation — <c>TryHarder</c> across every
///         symbology is what turns a 1080p frame into a visible pause.
///     </para>
/// </remarks>
public sealed class SlideTriggerScreenDecoder(ILogger<SlideTriggerScreenDecoder> logger)
{
    /// <summary>Returns null when the frame holds no marker at all; a decoded-but-invalid marker comes back as a failure result.</summary>
    public SlideTriggerPayloadResult? Decode(CapturedScreen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);

        try
        {
            var reader = new BarcodeReaderGeneric
            {
                AutoRotate = true,
                Options = new DecodingOptions
                {
                    TryHarder = true,
                    PossibleFormats = [BarcodeFormat.QR_CODE]
                }
            };

            var result = reader.Decode(
                Normalize(screen),
                screen.Width,
                screen.Height,
                RGBLuminanceSource.BitmapFormat.BGRA32);

            if (result?.Text is not { Length: > 0 } text)
                return null;

            var payload = SlideTriggerPayload.Decode(text, DateTimeOffset.UtcNow);

            // A QR code on a slide that is not ours is the ordinary case, not an error; debug keeps it out of the
            // classroom log while still being there when someone actually asks why a slide did nothing.
            if (!payload.Success)
                logger.LogDebug("屏幕上解出二维码，但不是课件标记：{Error}", payload.Error);

            return payload;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "解码课件标记失败");
            return null;
        }
    }

    /// <summary>
    ///     ZXing indexes the buffer as <c>width * 4</c> bytes per row, so a driver-padded stride has to be packed down
    ///     first. On the common case this returns the frame untouched rather than copying a multi-megabyte buffer.
    /// </summary>
    private static byte[] Normalize(CapturedScreen screen)
    {
        var stride = screen.Width * 4;
        if (screen.StrideBytes == stride)
            return screen.Bgra;

        var packed = new byte[stride * screen.Height];
        for (var row = 0; row < screen.Height; row++)
            Buffer.BlockCopy(screen.Bgra, row * screen.StrideBytes, packed, row * stride, stride);

        return packed;
    }
}
