using NWayland.Protocols.Wayland;

namespace Avalonia.Wayland
{
    internal unsafe class WlThemeCursor : WlCursor
    {
        private readonly AvaloniaWaylandPlatform _platform;
        private readonly LibWaylandCursor.wl_cursor* _wlCursor;
        private readonly WlCursorImage?[] _wlCursorImages;

        public WlThemeCursor(LibWaylandCursor.wl_cursor* wlCursor, AvaloniaWaylandPlatform platform) : base(wlCursor->image_count)
        {
            _platform = platform;
            _wlCursor = wlCursor;
            _wlCursorImages = new WlCursorImage[ImageCount];
        }

        public override WlCursorImage this[uint index]
        {
            get
            {
                var cachedImage = _wlCursorImages[index];
                if (cachedImage is not null)
                    return cachedImage;
                var image = _wlCursor->images[index];
                var rawBuffer = LibWaylandCursor.wl_cursor_image_get_buffer(image);
                var wlBuffer = new WlBuffer(rawBuffer, WlBuffer.InterfaceVersion, _platform.WlDisplay);
                return _wlCursorImages[index] = new WlCursorImage(wlBuffer, (int)image->hotspot_x, (int)image->hotspot_y);
            }
        }

        public override void Dispose()
        {
            foreach (var wlCursorImage in _wlCursorImages)
                wlCursorImage?.WlBuffer.Dispose();
        }
    }
}
