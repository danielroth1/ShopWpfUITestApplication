using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Automation;
using ShopTest.UiRunner.Interop;

namespace ShopTest.UiRunner.Ui;

/// <summary>
/// Best-effort visual evidence. A blank surface is reported, never thrown: scenario verdicts come from
/// UIA state and SQL, not from pixels.
/// </summary>
internal static class Capture
{
    public static bool TrySave(AutomationElement? target, string path, out string note)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        if (target is not null)
        {
            var window = RenderWindow(target);
            if (window is not null)
            {
                using (window)
                {
                    if (!IsBlank(window))
                    {
                        window.Save(path, ImageFormat.Png);
                        note = "printwindow";
                        return true;
                    }
                }
            }
        }

        var screen = RenderScreen(target);
        if (screen is not null)
        {
            using (screen)
            {
                if (!IsBlank(screen))
                {
                    screen.Save(path, ImageFormat.Png);
                    note = "screen";
                    return true;
                }
            }
        }

        note = "capture-unavailable: the guest surface returned blank pixels";
        return false;
    }

    private static Bitmap? RenderWindow(AutomationElement element)
    {
        try
        {
            var hwnd = new IntPtr(element.Current.NativeWindowHandle);
            var bounds = element.Current.BoundingRectangle;

            var width = (int)bounds.Width;
            var height = (int)bounds.Height;

            if (hwnd == IntPtr.Zero || width <= 1 || height <= 1)
            {
                return null;
            }

            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(bitmap);
            var hdc = graphics.GetHdc();

            try
            {
                if (!NativeMethods.TryPrintWindow(hwnd, hdc))
                {
                    bitmap.Dispose();
                    return null;
                }
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }

            return bitmap;
        }
        catch (Exception exception) when (exception is ElementNotAvailableException or InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    private static Bitmap? RenderScreen(AutomationElement? element)
    {
        try
        {
            Rectangle area;

            if (element is not null)
            {
                var bounds = element.Current.BoundingRectangle;
                area = bounds.IsEmpty
                    ? new Rectangle(0, 0, 1, 1)
                    : new Rectangle((int)bounds.X, (int)bounds.Y, (int)bounds.Width, (int)bounds.Height);
            }
            else
            {
                var (width, height) = NativeMethods.PrimaryScreenSize();
                area = new Rectangle(0, 0, width, height);
            }

            if (area.Width <= 1 || area.Height <= 1)
            {
                return null;
            }

            var bitmap = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(area.Location, Point.Empty, area.Size, CopyPixelOperation.SourceCopy);
            return bitmap;
        }
        catch (Exception exception) when (exception is ElementNotAvailableException or InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static bool IsBlank(Bitmap bitmap)
    {
        var first = bitmap.GetPixel(0, 0);
        var stepX = Math.Max(1, bitmap.Width / 8);
        var stepY = Math.Max(1, bitmap.Height / 8);

        for (var y = 0; y < bitmap.Height; y += stepY)
        {
            for (var x = 0; x < bitmap.Width; x += stepX)
            {
                if (bitmap.GetPixel(x, y) != first)
                {
                    return false;
                }
            }
        }

        return true;
    }
}
