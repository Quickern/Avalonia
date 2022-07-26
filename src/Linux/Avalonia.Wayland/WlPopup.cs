using System;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input.Raw;
using Avalonia.Platform;
using NWayland.Protocols.XdgShell;

namespace Avalonia.Wayland
{
    internal class WlPopup : WlWindow, IPopupImpl, IPopupPositioner, XdgPopup.IEvents, XdgPositioner.IEvents
    {
        private readonly AvaloniaWaylandPlatform _platform;
        private readonly XdgPositioner _xdgPositioner;

        private XdgPopup? _xdgPopup;
        private uint _repositionToken;

        internal WlPopup(AvaloniaWaylandPlatform platform, WlWindow parent) : base(platform)
        {
            _platform = platform;
            _xdgPositioner = platform.XdgWmBase.CreatePositioner();
            Parent = parent;
        }

        public IPopupPositioner PopupPositioner => this;

        public void SetWindowManagerAddShadowHint(bool enabled) { }

        public override void Show(bool activate, bool isDialog)
        {
            if (_xdgPopup is null)
            {
                _xdgPopup = XdgSurface.GetPopup(Parent!.XdgSurface, _xdgPositioner);
                _xdgPopup.Events = this;
                _xdgPopup.Grab(_platform.WlSeat, _platform.WlInputDevice.Serial);
            }

            base.Show(activate, isDialog);
        }

        public void Update(PopupPositionerParameters parameters)
        {
            Resize(parameters.Size);
            _xdgPositioner.SetReactive();
            _xdgPositioner.SetAnchor(ParsePopupAnchor(parameters.Anchor));
            _xdgPositioner.SetGravity(ParsePopupGravity(parameters.Gravity));
            _xdgPositioner.SetOffset((int)parameters.Offset.X, (int)parameters.Offset.Y);
            _xdgPositioner.SetSize((int)parameters.Size.Width, (int)parameters.Size.Height);
            _xdgPositioner.SetAnchorRect((int)Math.Ceiling(parameters.AnchorRectangle.X), (int)Math.Ceiling(parameters.AnchorRectangle.Y), (int)Math.Ceiling(parameters.AnchorRectangle.Width), (int)Math.Ceiling(parameters.AnchorRectangle.Height));
            _xdgPositioner.SetConstraintAdjustment((uint)(XdgPositioner.ConstraintAdjustmentEnum)parameters.ConstraintAdjustment);
            if (_xdgPopup is null || XdgSurfaceConfigureSerial == 0)
                return;
            _xdgPositioner.SetParentConfigure(Parent!.XdgSurfaceConfigureSerial);
            _xdgPopup.Reposition(_xdgPositioner, ++_repositionToken);
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

        public void OnPopupDone(XdgPopup eventSender)
        {
            if (_platform.WlInputDevice.MouseDevice is null || InputRoot is null)
                return;
            var args = new RawPointerEventArgs(_platform.WlInputDevice.MouseDevice, 0, InputRoot, RawPointerEventType.NonClientLeftButtonDown, new Point(), _platform.WlInputDevice.RawInputModifiers);
            Input?.Invoke(args);
        }

        public void OnRepositioned(XdgPopup eventSender, uint token) { }

        public override void Dispose()
        {
            Closed?.Invoke();
            _xdgPositioner.Dispose();
            _xdgPopup?.Dispose();
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
