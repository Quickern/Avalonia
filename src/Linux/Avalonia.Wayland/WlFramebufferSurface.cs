using System;
using Avalonia.Controls.Platform.Surfaces;
using Avalonia.FreeDesktop;
using Avalonia.Platform;
using NWayland.Interop;
using NWayland.Protocols.Wayland;

namespace Avalonia.Wayland
{
    internal class WlFramebufferSurface : IFramebufferPlatformSurface, IDisposable
    {
        private readonly AvaloniaWaylandPlatform _platform;
        private readonly WlWindow _wlWindow;
        private readonly WlSurface _wlSurface;

        private int _fd;
        private int _size;
        private WlShmPool? _wlShmPool;
        private WlBuffer? _wlBuffer;
        private IntPtr _data;

        public WlFramebufferSurface(AvaloniaWaylandPlatform platform, WlWindow wlWindow, WlSurface wlSurface)
        {
            _platform = platform;
            _wlWindow = wlWindow;
            _wlSurface = wlSurface;
        }

        public ILockedFramebuffer Lock()
        {
            var width = (int)_wlWindow.ClientSize.Width;
            var height = (int)_wlWindow.ClientSize.Height;
            var stride = width * 4;
            var size = height * stride;

            if (_wlShmPool is null)
            {
                _fd = FdHelper.CreateAnonymousFile(size);
                if (_fd == -1)
                    throw new NWaylandException("Failed to create FrameBuffer");
                _data = NativeMethods.mmap(IntPtr.Zero, new IntPtr(size), NativeMethods.PROT_READ | NativeMethods.PROT_WRITE, NativeMethods.MAP_SHARED, _fd, IntPtr.Zero);
                _wlShmPool= _platform.WlShm.CreatePool(_fd, size);
                _wlBuffer = _wlShmPool.CreateBuffer(0, width, height, stride, WlShm.FormatEnum.Argb8888);
                _size = size;
            }

            if (size != _size)
            {
                _wlBuffer!.Dispose();
                NativeMethods.munmap(_data, new IntPtr(_size));
                _fd = FdHelper.ResizeFd(_fd, size);
                if (_fd == -1)
                    throw new NWaylandException("Failed to create FrameBuffer");
                _data = NativeMethods.mmap(IntPtr.Zero, new IntPtr(size), NativeMethods.PROT_READ | NativeMethods.PROT_WRITE, NativeMethods.MAP_SHARED, _fd, IntPtr.Zero);
                _wlShmPool.Resize(size);
                _wlBuffer = _wlShmPool.CreateBuffer(0, width, height, stride, WlShm.FormatEnum.Argb8888);
                _size = size;
            }

            return new WlFramebuffer(_wlSurface, _wlBuffer!, _data, new PixelSize(width, height), stride, PixelFormat.Bgra8888);
        }

        public void Dispose()
        {
            _wlShmPool?.Dispose();
            _wlBuffer?.Dispose();
        }
    }
}
