using Avalonia.Input;
using Avalonia.Platform;

#nullable enable

namespace Avalonia.Wayland
{
    class WaylandCursorFactory : ICursorFactory
    {
        private static IntPtr _nullCursor;

        public ICursorImpl CreateCursor(IBitmapImpl cursor, PixelPoint hotSpot)
        {
            return new WaylandCursor(cursor, hotSpot);
        }

        public ICursorImpl GetCursor(StandardCursorType cursorType)
        {
            IntPtr handle = _nullCursor;

            if(cursorType == StandardCursorType.None)
            {
                handle = _nullCursor;
            }

            return new CursorImpl(handle);
        }

        private unsafe class WaylandCursor : CursorImpl
        {
            private readonly PixelSize _pixelsize;

            public WaylandCursor(IBitmapImpl bitmap, PixelPoint hotSpot)
            {

            }

        }

        class CursorImpl : ICursorImpl
        {
            public CursorImpl() { }
            public CursorImpl(IntPtr handle) => Handle = handle;
            public IntPtr Handle { get; protected set; }
            public void Dispose() { }
        }
    }

}
