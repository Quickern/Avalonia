using Avalonia.Platform;
using NWayland.Protocols.Wayland;

namespace Avalonia.Wayland
{
    internal abstract class WlCursor : ICursorImpl
    {
        protected WlCursor(uint imageCount)
        {
            ImageCount = imageCount;
        }

        public abstract WlCursorImage? this[uint index]
        {
            get;
        }

        public uint ImageCount { get; }

        public abstract void Dispose();

        public class WlCursorImage
        {
            public WlCursorImage(WlBuffer wlBuffer, int hotspotX, int hotspotY)
            {
                WlBuffer = wlBuffer;
                HotspotX = hotspotX;
                HotspotY = hotspotY;
            }

            public WlBuffer WlBuffer { get; }
            public int HotspotX { get; }
            public int HotspotY { get; }
        }
    }
}
