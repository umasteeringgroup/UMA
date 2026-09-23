#if UNITY_EDITOR_WIN
using System;
using System.Runtime.InteropServices;

namespace UMA.CharacterSystem.Editors
{
    // A click-through, non-activating owned window. Alpha is zero inside the popup, so
    // the shadow composites over the underlying desktop only, never over popup content.
    internal sealed class NativePopupShadow : IDisposable
    {
        private IntPtr owner;
        private IntPtr shadow;
        private Bounds previous;
        private uint previousDpi;
        private bool hasImage;

        internal NativePopupShadow(IntPtr popup)
        {
            owner = popup;
            const uint extendedStyle = 0x00080000 | 0x00000020 | 0x00000080 | 0x08000000;
            shadow = CreateWindowEx(extendedStyle, "STATIC", "UMA popup shadow", 0x80000000,
                0, 0, 1, 1, owner, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (shadow == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        internal static NativePopupShadow TryAttach(int expectedWidth, int expectedHeight)
        {
            // Unity can focus a child view while the owning top-level popup is active.
            IntPtr popup = GetAncestor(GetFocus(), 2); // GA_ROOT
            if (!MatchesPopup(popup, expectedWidth, expectedHeight)) popup = GetActiveWindow();
            return MatchesPopup(popup, expectedWidth, expectedHeight) ? new NativePopupShadow(popup) : null;
        }

        private static bool MatchesPopup(IntPtr popup, int expectedWidth, int expectedHeight)
        {
            uint process;
            GetWindowThreadProcessId(popup, out process);
            Bounds rect;
            // Called only while the Unity PopupWindow is focused. Also verify native ownership
            // and dimensions to avoid attaching to the main editor or another application's window.
            return popup != IntPtr.Zero && process == GetCurrentProcessId() && GetWindowRect(popup, out rect) &&
                Math.Abs(rect.Right - rect.Left - expectedWidth) <= 8 &&
                Math.Abs(rect.Bottom - rect.Top - expectedHeight) <= 8;
        }

        internal void Update()
        {
            if (shadow == IntPtr.Zero) return;
            if (!IsWindow(owner)) { Dispose(); return; }
            if (!IsWindowVisible(owner) || IsIconic(owner)) { ShowWindow(shadow, 0); return; }
            Bounds rect;
            if (!GetWindowRect(owner, out rect)) return;
            uint dpi = GetDpiForWindow(owner);
            if (dpi == 0) dpi = 96;
            int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0) return;
            float scale = dpi / 96f;
            int padding = (int)Math.Ceiling(20 * scale);
            if (!hasImage || width != previous.Right - previous.Left || height != previous.Bottom - previous.Top || dpi != previousDpi)
            {
                Render(rect, padding, scale);
                hasImage = true;
            }
            // NOACTIVATE | NOZORDER | SHOWWINDOW: no focus changes and no global topmost window.
            SetWindowPos(shadow, IntPtr.Zero, rect.Left - padding, rect.Top - padding,
                width + padding * 2, height + padding * 2, 0x0010 | 0x0004 | 0x0040);
            previous = rect;
            previousDpi = dpi;
        }

        internal static byte[] CreatePixels(int popupWidth, int popupHeight, int padding, float scale)
        {
            int width = popupWidth + padding * 2, height = popupHeight + padding * 2;
            byte[] pixels = new byte[checked(width * height * 4)];
            double sigma = 5 * scale;
            double left = padding + 4 * scale, top = padding + 5 * scale;
            var horizontal = new double[width];
            for (int x = 0; x < width; x++)
                horizontal[x] = GaussianStep((x + .5 - left) / sigma) - GaussianStep((x + .5 - left - popupWidth) / sigma);
            for (int y = 0; y < height; y++)
            {
                double vertical = GaussianStep((y + .5 - top) / sigma) - GaussianStep((y + .5 - top - popupHeight) / sigma);
                for (int x = 0; x < width; x++)
                {
                    if (x >= padding && x < padding + popupWidth && y >= padding && y < padding + popupHeight) continue;
                    // Premultiplied BGRA: black RGB is already zero; only alpha needs writing.
                    pixels[(y * width + x) * 4 + 3] = (byte)Math.Max(0, Math.Min(255, Math.Round(90 * horizontal[x] * vertical)));
                }
            }
            return pixels;
        }

        private static double GaussianStep(double value)
        {
            // Normal CDF approximation, giving smooth edges and rounded shadow corners.
            double x = Math.Abs(value) / Math.Sqrt(2);
            double t = 1 / (1 + .3275911 * x);
            double erf = 1 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - .284496736) * t + .254829592) * t * Math.Exp(-x * x);
            return .5 * (1 + (value < 0 ? -erf : erf));
        }

        private void Render(Bounds rect, int padding, float scale)
        {
            int popupWidth = rect.Right - rect.Left, popupHeight = rect.Bottom - rect.Top;
            int width = popupWidth + padding * 2, height = popupHeight + padding * 2;
            byte[] pixels = CreatePixels(popupWidth, popupHeight, padding, scale);
            IntPtr dc = CreateCompatibleDC(IntPtr.Zero);
            if (dc == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            IntPtr bitmap = IntPtr.Zero, oldBitmap = IntPtr.Zero;
            try
            {
                BitmapInfo info = new BitmapInfo();
                info.Header.Size = (uint)Marshal.SizeOf(typeof(BitmapHeader));
                info.Header.Width = width;
                info.Header.Height = -height; // top-down DIB
                info.Header.Planes = 1;
                info.Header.BitCount = 32;
                IntPtr bits;
                bitmap = CreateDIBSection(dc, ref info, 0, out bits, IntPtr.Zero, 0);
                if (bitmap == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                Marshal.Copy(pixels, 0, bits, pixels.Length);
                oldBitmap = SelectObject(dc, bitmap);
                Point destination = new Point(rect.Left - padding, rect.Top - padding);
                Point source = new Point(0, 0);
                Size size = new Size(width, height);
                Blend blend = new Blend { SourceConstantAlpha = 255, AlphaFormat = 1 };
                if (!UpdateLayeredWindow(shadow, IntPtr.Zero, ref destination, ref size, dc, ref source, 0, ref blend, 2))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
            finally
            {
                if (oldBitmap != IntPtr.Zero) SelectObject(dc, oldBitmap);
                if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
                DeleteDC(dc);
            }
        }

        public void Dispose()
        {
            if (shadow != IntPtr.Zero && IsWindow(shadow)) DestroyWindow(shadow);
            shadow = IntPtr.Zero;
            owner = IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Sequential)] private struct Bounds { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct Point
        {
            public int X, Y;
            public Point(int x, int y) { X = x; Y = y; }
        }
        [StructLayout(LayoutKind.Sequential)] private struct Size
        {
            public int Width, Height;
            public Size(int width, int height) { Width = width; Height = height; }
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct Blend { public byte Operation, Flags, SourceConstantAlpha, AlphaFormat; }
        [StructLayout(LayoutKind.Sequential)] private struct BitmapHeader
        {
            public uint Size;
            public int Width, Height;
            public ushort Planes, BitCount;
            public uint Compression, SizeImage;
            public int XPelsPerMeter, YPelsPerMeter;
            public uint ColorsUsed, ColorsImportant;
        }
        [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo { public BitmapHeader Header; public uint Colors; }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(uint exStyle, string className, string title, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
        [DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetFocus();
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentProcessId();
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Bounds rect);
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr window);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr screenDC, ref Point destination, ref Size size, IntPtr sourceDC, ref Point source, uint colorKey, ref Blend blend, uint flags);
        [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    }
}
#endif
