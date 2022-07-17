using System;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Platform;
using NWayland.Protocols.XdgShell;

namespace Avalonia.Wayland
{
    internal class WlPopup : WlWindow, IPopupImpl, IPopupPositioner, XdgPopup.IEvents, XdgPositioner.IEvents
    {
        private readonly WlWindow _parent;
        private readonly XdgPositioner _xdgPositioner;

        private XdgPopup? _xdgPopup;

        internal WlPopup(AvaloniaWaylandPlatform platform, WlWindow parent) : base(platform)
        {
            _parent = parent;
            _xdgPositioner = platform.XdgWmBase.CreatePositioner();
            _xdgPositioner.SetReactive();
        }

        public IPopupPositioner PopupPositioner => this;

        public void SetWindowManagerAddShadowHint(bool enabled) { }

        public override void Show(bool activate, bool isDialog)
        {
            if (_xdgPopup is null)
            {
                _xdgPopup = XdgSurface.GetPopup(_parent.XdgSurface, _xdgPositioner);
                _xdgPopup.Events = this;
            }

            base.Show(activate, isDialog);
        }

        public void Update(PopupPositionerParameters parameters)
        {
            _xdgPositioner.SetAnchor(ParsePopupAnchor(parameters.Anchor));
            _xdgPositioner.SetGravity(ParsePopupGravity(parameters.Gravity));
            _xdgPositioner.SetOffset((int)parameters.Offset.X, (int)parameters.Offset.Y);
            var width = Math.Max(1, (int)parameters.Size.Width);
            var height = Math.Max(1, (int)parameters.Size.Height);
            _xdgPositioner.SetSize(width, height);
            var anchorWidth = Math.Max(1, (int)parameters.AnchorRectangle.Width);
            var anchorHeight = Math.Max(1, (int)parameters.AnchorRectangle.Height);
            _xdgPositioner.SetAnchorRect((int)parameters.AnchorRectangle.X, (int)parameters.AnchorRectangle.Y, anchorWidth, anchorHeight);
            _xdgPositioner.SetConstraintAdjustment((uint)(XdgPositioner.ConstraintAdjustmentEnum)parameters.ConstraintAdjustment);
            _xdgPositioner.SetParentConfigure(_parent.XdgSurfaceConfigureSerial);
            _xdgPopup?.Reposition(_xdgPositioner, _parent.XdgSurfaceConfigureSerial);
        }

        public void OnConfigure(XdgPopup eventSender, int x, int y, int width, int height)
        {
            PendingSize = new PixelSize(width, height);
            var position = new PixelPoint(x, y);
            if (position == Position)
                return;
            Position = position;
            PositionChanged?.Invoke(Position);
        }

        public void OnPopupDone(XdgPopup eventSender) => Dispose();

        public void OnRepositioned(XdgPopup eventSender, uint token) { }

        public override void Dispose()
        {
            Closed?.Invoke();
            _xdgPositioner.Dispose();
            _xdgPopup?.Dispose();
            _xdgPopup = null;
            base.Dispose();
        }

        private static XdgPositioner.AnchorEnum ParsePopupAnchor(PopupAnchor popupAnchor) => popupAnchor switch
        {
            PopupAnchor.TopLeft => XdgPositioner.AnchorEnum.TopLeft,
            PopupAnchor.TopRight => XdgPositioner.AnchorEnum.TopRight,
            PopupAnchor.BottomLeft => XdgPositioner.AnchorEnum.BottomLeft,
            PopupAnchor.BottomRight => XdgPositioner.AnchorEnum.BottomRight,
            PopupAnchor.Top => XdgPositioner.AnchorEnum.Top,
            PopupAnchor.Left => XdgPositioner.AnchorEnum.Left,
            PopupAnchor.Bottom => XdgPositioner.AnchorEnum.Bottom,
            PopupAnchor.Right => XdgPositioner.AnchorEnum.Right,
            _ => XdgPositioner.AnchorEnum.None
        };

        private static XdgPositioner.GravityEnum ParsePopupGravity(PopupGravity popupGravity) => popupGravity switch
        {
            PopupGravity.TopLeft => XdgPositioner.GravityEnum.TopLeft,
            PopupGravity.TopRight => XdgPositioner.GravityEnum.TopRight,
            PopupGravity.BottomLeft => XdgPositioner.GravityEnum.BottomLeft,
            PopupGravity.BottomRight => XdgPositioner.GravityEnum.BottomRight,
            PopupGravity.Top => XdgPositioner.GravityEnum.Top,
            PopupGravity.Left => XdgPositioner.GravityEnum.Left,
            PopupGravity.Bottom => XdgPositioner.GravityEnum.Bottom,
            PopupGravity.Right => XdgPositioner.GravityEnum.Right,
            _ => XdgPositioner.GravityEnum.None
        };
    }
}
