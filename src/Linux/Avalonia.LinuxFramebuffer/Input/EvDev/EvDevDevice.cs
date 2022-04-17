using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia.FreeDesktop;

namespace Avalonia.LinuxFramebuffer.Input.EvDev
{
    unsafe class EvDevDevice
    {
        public int Fd { get; }
        private IntPtr _dev;
        public string Name { get; }
        public List<EvType> EventTypes { get; private set; } = new List<EvType>();
        public input_absinfo? AbsX { get; }
        public input_absinfo? AbsY { get; }

        public EvDevDevice(int fd, IntPtr dev)
        {
            Fd = fd;
            _dev = dev;
            Name = Marshal.PtrToStringAnsi(NativeMethods.libevdev_get_name(_dev));
            foreach (EvType type in Enum.GetValues(typeof(EvType)))
            {
                if (NativeMethods.libevdev_has_event_type(dev, type) != 0)
                    EventTypes.Add(type);
            }
            var ptr = NativeMethods.libevdev_get_abs_info(dev, (int) AbsAxis.ABS_X);
            if (ptr != null)
                AbsX = *ptr;
            ptr = NativeMethods.libevdev_get_abs_info(dev, (int)AbsAxis.ABS_Y);
            if (ptr != null)
                AbsY = *ptr;
        }
        
        public input_event? NextEvent()
        {
            input_event ev;
            if (NativeMethods.libevdev_next_event(_dev, 2, out ev) == 0)
                return ev;
            return null;
        }

        public static EvDevDevice Open(string device)
        {
            var fd = NativeMethods.open(device, 2048, 0);
            if (fd <= 0)
                throw new Exception($"Unable to open {device} code {Marshal.GetLastWin32Error()}");
            IntPtr dev;
            var rc = NativeMethods.libevdev_new_from_fd(fd, out dev);
            if (rc < 0)
            {
                NativeMethods.close(fd);
                throw new Exception($"Unable to initialize evdev for {device} code {Marshal.GetLastWin32Error()}");
            }
            return new EvDevDevice(fd, dev);
        }
    }

    internal class EvDevAxisInfo
    {
        public int Minimum { get; set; }
        public int Maximum { get; set; }
    }
}
