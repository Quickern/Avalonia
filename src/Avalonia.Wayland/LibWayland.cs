using System;
using System.Runtime.InteropServices;

namespace Avalonia.Wayland
{
    public static class LibWayland
    {
        private const string libWaylandCursor = "libwayland-cursor.so.0";
        private const string libWaylandEgl = "libwayland-egl.so.1";

        [DllImport(libWaylandCursor)]
        public static extern IntPtr wl_cursor_theme_load(string? name, int size, IntPtr shm);

        [DllImport(libWaylandCursor)]
        public static extern void wl_cursor_theme_destroy(IntPtr theme);

        [DllImport(libWaylandCursor)]
        public static extern IntPtr wl_cursor_theme_get_cursor(IntPtr theme, string? name);

        [DllImport(libWaylandEgl)]
        public static extern IntPtr wl_egl_window_create(IntPtr surface, int width, int height);

        [DllImport(libWaylandEgl)]
        public static extern void wl_egl_window_destroy(IntPtr window);

        [DllImport(libWaylandEgl)]
        public static extern void wl_egl_window_resize(IntPtr window, int width, int height, int x, int y);
    }
}
