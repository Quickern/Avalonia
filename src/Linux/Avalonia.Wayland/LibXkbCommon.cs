using System;
using System.Runtime.InteropServices;

namespace Avalonia.Wayland
{
    internal static class LibXkbCommon
    {
        private const string XkbCommon = "libxkbcommon.so.0";

        [DllImport(XkbCommon)]
        public static extern IntPtr xkb_context_new(int flags);

        [DllImport(XkbCommon)]
        public static extern IntPtr xkb_keymap_new_from_string(IntPtr context, IntPtr @string, uint format, uint flags);

        [DllImport(XkbCommon)]
        public static extern IntPtr xkb_state_new(IntPtr keymap);

        [DllImport(XkbCommon)]
        public static extern void xkb_state_update_mask(IntPtr state, uint modsDepressed, uint modsLatched, uint modsLocked, uint layoutDepressed, uint layoutLatched, uint layoutLocked);

        [DllImport(XkbCommon)]
        public static extern uint xkb_state_serialize_mods(IntPtr state, XkbStateComponent components);

        [DllImport(XkbCommon)]
        public static extern void xkb_keymap_unref(IntPtr keymap);

        [DllImport(XkbCommon)]
        public static extern uint xkb_keymap_mod_get_index(IntPtr keymap, string name);

        [DllImport(XkbCommon)]
        public static extern bool xkb_keymap_key_repeats(IntPtr keymap, uint key);

        [DllImport(XkbCommon)]
        public static extern int xkb_keysym_to_utf8(uint sym, IntPtr buffer, uint size);

        [DllImport(XkbCommon)]
        public static extern void xkb_state_unref(IntPtr state);

        [DllImport(XkbCommon)]
        public static extern void xkb_context_unref(IntPtr context);

        [DllImport(XkbCommon)]
        public static extern unsafe uint xkb_state_key_get_syms(IntPtr state, uint code, uint** syms);

        [Flags]
        public enum XkbStateComponent
        {
            XKB_STATE_MODS_DEPRESSED = 1,
            XKB_STATE_MODS_LATCHED = 2,
            XKB_STATE_MODS_LOCKED = 4,
            XKB_STATE_MODS_EFFECTIVE = 8,
            XKB_STATE_LAYOUT_DEPRESSED = 16,
            XKB_STATE_LAYOUT_LATCHED = 32,
            XKB_STATE_LAYOUT_LOCKED = 64,
            XKB_STATE_LAYOUT_EFFECTIVE = 128,
            XKB_STATE_LEDS = 256
        }
    }
}
