using System;
using System.Runtime.InteropServices;

namespace Avalonia.FreeDesktop
{
    internal static class LibEvDev
    {
        private const string EvDev = "libevdev.so.2";

        [DllImport(EvDev, SetLastError = true)]
        public static extern int libevdev_new_from_fd(int fd, out IntPtr dev);

        [DllImport(EvDev, SetLastError = true)]
        public static extern int libevdev_has_event_type(IntPtr dev, EvType type);

        [DllImport(EvDev, SetLastError = true)]
        public static extern int libevdev_next_event(IntPtr dev, int flags, out input_event ev);

        [DllImport(EvDev, SetLastError = true)]
        public static extern IntPtr libevdev_get_name(IntPtr dev);

        [DllImport(EvDev, SetLastError = true)]
        public static extern unsafe input_absinfo* libevdev_get_abs_info(IntPtr dev, int code);
    }

    public enum FbIoCtl : uint
    {
        FBIOGET_VSCREENINFO = 0x4600,
        FBIOPUT_VSCREENINFO = 0x4601,
        FBIOGET_FSCREENINFO = 0x4602,
        FBIOGET_VBLANK = 0x80204612u,
        FBIO_WAITFORVSYNC = 0x40044620,
        FBIOPAN_DISPLAY = 0x4606
    }

    [Flags]
    public enum VBlankFlags
    {
        FB_VBLANK_VBLANKING = 0x001 /* currently in a vertical blank */,
        FB_VBLANK_HBLANKING = 0x002 /* currently in a horizontal blank */,
        FB_VBLANK_HAVE_VBLANK = 0x004 /* vertical blanks can be detected */,
        FB_VBLANK_HAVE_HBLANK = 0x008 /* horizontal blanks can be detected */,
        FB_VBLANK_HAVE_COUNT = 0x010 /* global retrace counter is available */,
        FB_VBLANK_HAVE_VCOUNT = 0x020 /* the vcount field is valid */,
        FB_VBLANK_HAVE_HCOUNT = 0x040 /* the hcount field is valid */,
        FB_VBLANK_VSYNCING = 0x080 /* currently in a vsync */,
        FB_VBLANK_HAVE_VSYNC = 0x100 /* vertical syncs can be detected */
    }

    public enum EvType
    {
        EV_SYN = 0x00,
        EV_KEY = 0x01,
        EV_REL = 0x02,
        EV_ABS = 0x03,
        EV_MSC = 0x04,
        EV_SW = 0x05,
        EV_LED = 0x11,
        EV_SND = 0x12,
        EV_REP = 0x14,
        EV_FF = 0x15,
        EV_PWR = 0x16,
        EV_FF_STATUS = 0x17
    }

    public enum AxisEventCode
    {
        REL_X = 0x00,
        REL_Y = 0x01,
        REL_Z = 0x02,
        REL_RX = 0x03,
        REL_RY = 0x04,
        REL_RZ = 0x05,
        REL_HWHEEL = 0x06,
        REL_DIAL = 0x07,
        REL_WHEEL = 0x08,
        REL_MISC = 0x09,
        REL_MAX = 0x0f
    }

    public enum AbsAxis
    {
        ABS_X = 0x00,
        ABS_Y = 0x01,
        ABS_Z = 0x02,
        ABS_RX = 0x03,
        ABS_RY = 0x04,
        ABS_RZ = 0x05,
        ABS_THROTTLE = 0x06,
        ABS_RUDDER = 0x07,
        ABS_WHEEL = 0x08,
        ABS_GAS = 0x09,
        ABS_BRAKE = 0x0a,
        ABS_HAT0X = 0x10,
        ABS_HAT0Y = 0x11,
        ABS_HAT1X = 0x12,
        ABS_HAT1Y = 0x13,
        ABS_HAT2X = 0x14,
        ABS_HAT2Y = 0x15,
        ABS_HAT3X = 0x16,
        ABS_HAT3Y = 0x17,
        ABS_PRESSURE = 0x18,
        ABS_DISTANCE = 0x19,
        ABS_TILT_X = 0x1a,
        ABS_TILT_Y = 0x1b,
        ABS_TOOL_WIDTH = 0x1c
    }

    public enum EvKey
    {
        BTN_LEFT = 0x110,
        BTN_RIGHT = 0x111,
        BTN_MIDDLE = 0x112,
        BTN_SIDE = 0x113,
        BTN_EXTRA = 0x114,
        BTN_TOUCH = 0x14a
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct fb_vblank
    {
        public VBlankFlags flags;			/* FB_VBLANK flags */
        uint count;			/* counter of retraces since boot */
        uint vcount;			/* current scanline position */
        uint hcount;			/* current scandot position */
        fixed uint reserved[4];		/* reserved for future compatibility */
    };

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct fb_fix_screeninfo
    {
        public fixed byte id[16]; /* identification string eg "TT Builtin" */

        public IntPtr smem_start; /* Start of frame buffer mem */

        /* (physical address) */
        public uint smem_len; /* Length of frame buffer mem */

        public uint type; /* see FB_TYPE_*		*/
        public uint type_aux; /* Interleave for interleaved Planes */
        public uint visual; /* see FB_VISUAL_*		*/
        public ushort xpanstep; /* zero if no hardware panning  */
        public ushort ypanstep; /* zero if no hardware panning  */
        public ushort ywrapstep; /* zero if no hardware ywrap    */
        public uint line_length; /* length of a line in bytes    */

        public IntPtr mmio_start; /* Start of Memory Mapped I/O   */

        /* (physical address) */
        public uint mmio_len; /* Length of Memory Mapped I/O  */

        public uint accel; /* Type of acceleration available */
        public fixed ushort reserved[3]; /* Reserved for future compatibility */
    };

    [StructLayout(LayoutKind.Sequential)]
    public struct fb_bitfield
    {
        public uint offset; /* beginning of bitfield	*/
        public uint length; /* length of bitfield		*/

        public uint msb_right; /* != 0 : Most significant bit is */
        /* right */
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct fb_var_screeninfo
    {
        public uint xres; /* visible resolution		*/
        public uint yres;
        public uint xres_virtual; /* virtual resolution		*/
        public uint yres_virtual;
        public uint xoffset; /* offset from virtual to visible */
        public uint yoffset; /* resolution			*/

        public uint bits_per_pixel; /* guess what			*/
        public uint grayscale; /* != 0 Graylevels instead of colors */

        public fb_bitfield red; /* bitfield in fb mem if true color, */
        public fb_bitfield green; /* else only length is significant */
        public fb_bitfield blue;
        public fb_bitfield transp; /* transparency			*/

        public uint nonstd; /* != 0 Non standard pixel format */

        public uint activate; /* see FB_ACTIVATE_*		*/

        public uint height; /* height of picture in mm    */
        public uint width; /* width of picture in mm     */

        public uint accel_flags; /* acceleration flags (hints)	*/

        /* Timing: All values in pixclocks, except pixclock (of course) */
        public uint pixclock; /* pixel clock in ps (pico seconds) */

        public uint left_margin; /* time from sync to picture	*/
        public uint right_margin; /* time from picture to sync	*/
        public uint upper_margin; /* time from sync to picture	*/
        public uint lower_margin;
        public uint hsync_len; /* length of horizontal sync	*/
        public uint vsync_len; /* length of vertical sync	*/
        public uint sync; /* see FB_SYNC_*		*/
        public uint vmode; /* see FB_VMODE_*		*/
        public fixed uint reserved[6]; /* Reserved for future compatibility */
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct input_event
    {
        private IntPtr timeval1, timeval2;
        public ushort _type, _code;
        public int value;
        public EvType Type => (EvType)_type;
        public EvKey Key => (EvKey)_code;
        public AbsAxis Axis => (AbsAxis)_code;

        public ulong Timestamp
        {
            get
            {
                var ms = (ulong)timeval2.ToInt64() / 1000;
                var s = (ulong)timeval1.ToInt64() * 1000;
                return s + ms;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct fd_set
    {
        public int count;
        public fixed byte fds [256];
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct input_absinfo
    {
        public int value;
        public int minimum;
        public int maximum;
        public int fuzz;
        public int flat;
        public int resolution;

    }
}
