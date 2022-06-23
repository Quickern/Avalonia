using System;
using System.Collections.Generic;
using System.Linq;
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
        private readonly List<ResizableBuffer> _buffers;

        public WlFramebufferSurface(AvaloniaWaylandPlatform platform, WlWindow wlWindow, WlSurface wlSurface)
        {
            _platform = platform;
            _wlWindow = wlWindow;
            _wlSurface = wlSurface;
            _buffers = new List<ResizableBuffer>();
        }

        public ILockedFramebuffer Lock()
        {
            var width = (int)_wlWindow.ClientSize.Width;
            var height = (int)_wlWindow.ClientSize.Height;
            var stride = width * 4;

            var buffer = _buffers.FirstOrDefault(static x => x.Available);
            if (buffer is null)
            {
                buffer = new ResizableBuffer(_platform);
                _buffers.Add(buffer);
            }

            return buffer.GetFramebuffer(_wlSurface, width, height, stride);
        }

        public void Dispose()
        {
            foreach (var buffer in _buffers)
                buffer.Dispose();
        }

        private sealed class ResizableBuffer : WlBuffer.IEvents, IDisposable
        {
            private readonly AvaloniaWaylandPlatform _platform;

            private int _size;
            private IntPtr _data;
            private WlBuffer? _wlBuffer;

            public ResizableBuffer(AvaloniaWaylandPlatform platform)
            {
                _platform = platform;
            }

            public bool Available { get; private set; }

            public WlFramebuffer GetFramebuffer(WlSurface surface, int width, int height, int stride)
            {
                Available = false;
                var size = stride * height;

                if (_size != size)
                {
                    _wlBuffer?.Dispose();
                    _wlBuffer = null;
                }

                if (_wlBuffer is null)
                {
                    var fd = FdHelper.CreateAnonymousFile(size);
                    if (fd == -1)
                        throw new NWaylandException("Failed to create FrameBuffer");
                    _data = LibC.mmap(IntPtr.Zero, new IntPtr(size), MemoryProtection.PROT_READ | MemoryProtection.PROT_WRITE, SharingType.MAP_SHARED, fd, IntPtr.Zero);
                    var wlShmPool = _platform.WlShm.CreatePool(fd, size);
                    _wlBuffer = wlShmPool.CreateBuffer(0, width, height, stride, WlShm.FormatEnum.Argb8888);
                    _wlBuffer.Events = this;
                    _size = size;
                    LibC.close(fd);
                }

                return new WlFramebuffer(surface, _wlBuffer!, _data, new PixelSize(width, height), stride, PixelFormat.Bgra8888);
            }

            public void OnRelease(WlBuffer eventSender)
            {
                Available = true;
            }

            public void Dispose()
            {
                _wlBuffer?.Dispose();
                LibC.munmap(_data, new IntPtr(_size));
            }
        }
    }
}
