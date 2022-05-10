using System;
using System.Runtime.InteropServices;

namespace Avalonia.Wayland
{
    internal static class LibWaylandCursor
    {
        private const string WaylandCursor = "libwayland-cursor.so.0";

        [DllImport(WaylandCursor)]
        internal static extern IntPtr wl_cursor_theme_load(string? name, int size, IntPtr shm);

        [DllImport(WaylandCursor)]
        internal static extern void wl_cursor_theme_destroy(IntPtr theme);

        [DllImport(WaylandCursor)]
        internal static extern IntPtr wl_cursor_theme_get_cursor(IntPtr theme, string? name);
    }
}
