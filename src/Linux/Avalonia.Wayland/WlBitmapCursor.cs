using System;
using Avalonia.Controls.Platform.Surfaces;
using Avalonia.FreeDesktop;
using Avalonia.Platform;
using Avalonia.Utilities;
using NWayland.Interop;
using NWayland.Protocols.Wayland;

namespace Avalonia.Wayland
{
    internal class WlBitmapCursor : WlCursor, IFramebufferPlatformSurface
    {
        private readonly IBitmapImpl _cursor;
        private readonly int _stride;
        private readonly int _fd;
        private readonly int _size;
        private readonly IntPtr _data;
        private readonly WlShmPool _wlShmPool;
        private readonly WlBuffer _wlBuffer;
        private readonly WlCursorImage _wlCursorImage;

        public WlBitmapCursor(AvaloniaWaylandPlatform platform, IBitmapImpl cursor, PixelPoint hotspot) : base(1)
        {
            _cursor = cursor;
            _stride = cursor.PixelSize.Width * 4;
            _size = cursor.PixelSize.Height * _stride;
            _fd = FdHelper.CreateAnonymousFile(_size);
            if (_fd == -1)
                throw new NWaylandException("Failed to create FrameBuffer");
            _data = NativeMethods.mmap(IntPtr.Zero, new IntPtr(_size), NativeMethods.PROT_READ | NativeMethods.PROT_WRITE, NativeMethods.MAP_SHARED, _fd, IntPtr.Zero);
            _wlShmPool= platform.WlShm.CreatePool(_fd, _size);
            _wlBuffer = _wlShmPool.CreateBuffer(0, cursor.PixelSize.Width, cursor.PixelSize.Height, _stride, WlShm.FormatEnum.Argb8888);
            var platformRenderInterface = AvaloniaLocator.Current.GetRequiredService<IPlatformRenderInterface>();
            using var renderTarget = platformRenderInterface.CreateRenderTarget(new[] { this });
            using var ctx = renderTarget.CreateDrawingContext(null);
            var r = new Rect(cursor.PixelSize.ToSize(1));
            ctx.DrawBitmap(RefCountable.CreateUnownedNotClonable(cursor), 1, r, r);
            _wlCursorImage = new WlCursorImage(_wlBuffer, cursor.PixelSize, hotspot, TimeSpan.Zero);
        }

        public override WlCursorImage this[int index] => _wlCursorImage;

        public override void Dispose()
        {
            _wlBuffer.Dispose();
            _wlShmPool.Dispose();
            if (_data != IntPtr.Zero)
                NativeMethods.munmap(_data, new IntPtr(_size));
            if (_fd != -1)
                NativeMethods.close(_fd);
        }

        public ILockedFramebuffer Lock() => new LockedFramebuffer(_data, _cursor.PixelSize, _stride, new Vector(96, 96), PixelFormat.Bgra8888, null);
    }
}
