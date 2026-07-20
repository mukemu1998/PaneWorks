using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace PaneWorks.Infrastructure.Windows;

internal sealed class DisplayIdentityResolver
{
    public string GetPhysicalId(Screen screen)
    {
        var monitor = GetMonitorDevice(screen.DeviceName);
        var rawIdentity = string.Join(
            "|",
            new[] { monitor.DeviceId, monitor.DeviceKey }
                .Where(value => !string.IsNullOrWhiteSpace(value)));

        if (string.IsNullOrWhiteSpace(rawIdentity))
        {
            rawIdentity = screen.DeviceName;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawIdentity.Trim()));
        return $"monitor-{Convert.ToHexString(hash[..12]).ToLowerInvariant()}";
    }

    private static (string DeviceId, string DeviceKey) GetMonitorDevice(string adapterName)
    {
        var device = CreateDisplayDevice();
        return EnumDisplayDevices(adapterName, 0, ref device, 0)
            ? (device.DeviceId, device.DeviceKey)
            : (string.Empty, string.Empty);
    }

    private static DisplayDevice CreateDisplayDevice()
    {
        return new DisplayDevice
        {
            Size = Marshal.SizeOf<DisplayDevice>()
        };
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EnumDisplayDevices(
        string? deviceName,
        uint deviceIndex,
        ref DisplayDevice displayDevice,
        uint flags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int Size;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;

        public int StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceId;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }
}
