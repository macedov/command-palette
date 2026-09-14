using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;

namespace CommandPalette;

internal static class ApplicationIconProvider
{
    private static readonly Lazy<ImageSource> WindowIcon =
        new(CreateImageSource);

    public static void ApplyTo(Window window)
    {
        window.Icon = WindowIcon.Value;
    }

    public static Drawing.Icon CreateIcon()
    {
        var executablePath = Environment.ProcessPath;

        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            var icon = Drawing.Icon.ExtractAssociatedIcon(executablePath);

            if (icon is not null)
                return icon;
        }

        return (Drawing.Icon)Drawing.SystemIcons.Application.Clone();
    }

    private static ImageSource CreateImageSource()
    {
        using var icon = CreateIcon();
        var imageSource = Imaging.CreateBitmapSourceFromHIcon(
            icon.Handle,
            Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions()
        );
        imageSource.Freeze();
        return imageSource;
    }
}
