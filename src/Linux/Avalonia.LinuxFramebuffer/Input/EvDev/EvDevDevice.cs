using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia.FreeDesktop;

namespace Avalonia.LinuxFramebuffer.Input.EvDev
{
    internal unsafe class EvDevDevice
    {
        private readonly IntPtr _dev;

        public int Fd { get; }
        public string Name { get; }
        public List<EvType> EventTypes { get; } = new();
        public input_absinfo? AbsX { get; }
        public input_absinfo? AbsY { get; }

        public EvDevDevice(int fd, IntPtr dev)
        {
            Fd = fd;
            _dev = dev;
            Name = Marshal.PtrToStringAnsi(LibEvDev.libevdev_get_name(_dev));
            foreach (EvType type in Enum.GetValues(typeof(EvType)))
            {
                if (LibEvDev.libevdev_has_event_type(dev, type) != 0)
                    EventTypes.Add(type);
            }
            var ptr = LibEvDev.libevdev_get_abs_info(dev, (int) AbsAxis.ABS_X);
            if (ptr != null)
                AbsX = *ptr;
            ptr = LibEvDev.libevdev_get_abs_info(dev, (int)AbsAxis.ABS_Y);
            if (ptr != null)
                AbsY = *ptr;
        }

        public input_event? NextEvent()
        {
            input_event ev;
            if (LibEvDev.libevdev_next_event(_dev, 2, out ev) == 0)
                return ev;
            return null;
        }

        public static EvDevDevice Open(string device)
        {
            var fd = LibC.open(device, 2048, 0);
            if (fd <= 0)
                throw new Exception($"Unable to open {device} code {Marshal.GetLastWin32Error()}");
            var rc = LibEvDev.libevdev_new_from_fd(fd, out var dev);
            if (rc < 0)
            {
                LibC.close(fd);
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
