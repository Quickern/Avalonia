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
        private bool _isLightDismissEnabled;

        internal WlPopup(AvaloniaWaylandPlatform platform, WlWindow parent) : base(platform)
        {
            _platform = platform;
            _xdgPositioner = platform.XdgWmBase.CreatePositioner();
            Parent = parent;
        }

        public IPopupPositioner PopupPositioner => this;

        public void SetWindowManagerAddShadowHint(bool enabled) { }

        public void SetIsLightDismissEnabledHint(bool enabled) => _isLightDismissEnabled = enabled;

        public override void Show(bool activate, bool isDialog)
        {
            if (_xdgPopup is null)
            {
                _xdgPopup = XdgSurface.GetPopup(Parent!.XdgSurface, _xdgPositioner);
                _xdgPopup.Events = this;
                if (_isLightDismissEnabled)
                {
                    _xdgPopup.Grab(_platform.WlSeat, _platform.WlInputDevice.UserActionDownSerial);
                    WlSurface.Commit();
                }
            }

            base.Show(activate, isDialog);
        }

        public void Update(PopupPositionerParameters parameters)
        {
            var size = new PixelSize((int)Math.Max(1, parameters.Size.Width), (int)Math.Max(1, parameters.Size.Height));
            Resize(new Size(size.Width, size.Height));
            _xdgPositioner.SetReactive();
            _xdgPositioner.SetAnchor(ParsePopupAnchor(parameters.Anchor));
            _xdgPositioner.SetGravity(ParsePopupGravity(parameters.Gravity));
            _xdgPositioner.SetOffset((int)parameters.Offset.X, (int)parameters.Offset.Y);
            _xdgPositioner.SetSize(size.Width, size.Height);
            _xdgPositioner.SetAnchorRect((int)Math.Max(1, parameters.AnchorRectangle.X), (int)Math.Max(1, parameters.AnchorRectangle.Y), (int)Math.Max(1, parameters.AnchorRectangle.Width), (int)Math.Max(1, parameters.AnchorRectangle.Height));
            _xdgPositioner.SetConstraintAdjustment((uint)parameters.ConstraintAdjustment);
            if (_xdgPopup is not null && XdgSurfaceConfigureSerial != 0)
            {
                _xdgPositioner.SetParentConfigure(Parent!.XdgSurfaceConfigureSerial);
                _xdgPopup.Reposition(_xdgPositioner, ++_repositionToken);
            }

            WlSurface.Commit();
        }

        public void OnConfigure(XdgPopup eventSender, int x, int y, int width, int height)
        {
            PendingSize = new Size(width, height);
            Position = new PixelPoint(x, y);
        }

        public void OnPopupDone(XdgPopup eventSender)
        {
            if (_platform.WlInputDevice.PointerHandler is null || InputRoot is null)
                return;
            var args = new RawPointerEventArgs(_platform.WlInputDevice.PointerHandler.MouseDevice, 0, InputRoot, RawPointerEventType.NonClientLeftButtonDown, new Point(), _platform.WlInputDevice.RawInputModifiers);
            Input?.Invoke(args);
        }

        public void OnRepositioned(XdgPopup eventSender, uint token) => PositionChanged?.Invoke(Position);

        public override void Dispose()
        {
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
