using System.Runtime.InteropServices;

namespace MicToggle;

internal static class WindowTheme
{
    private const int UseImmersiveDarkMode = 20;
    private const int BorderColor = 34;
    private const int CaptionColor = 35;
    private const int TextColor = 36;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        ref int attributeValue,
        int attributeSize);

    public static void ApplyDarkTitleBar(IntPtr windowHandle)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10))
        {
            return;
        }

        try
        {
            var enabled = 1;
            _ = SetAttribute(windowHandle, UseImmersiveDarkMode, enabled);

            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            {
                _ = SetAttribute(windowHandle, BorderColor, ToColorReference(48, 52, 58));
                _ = SetAttribute(windowHandle, CaptionColor, ToColorReference(24, 26, 29));
                _ = SetAttribute(windowHandle, TextColor, ToColorReference(242, 244, 247));
            }
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    private static int SetAttribute(IntPtr windowHandle, int attribute, int value)
    {
        return DwmSetWindowAttribute(windowHandle, attribute, ref value, sizeof(int));
    }

    private static int ToColorReference(byte red, byte green, byte blue)
    {
        return red | (green << 8) | (blue << 16);
    }
}
