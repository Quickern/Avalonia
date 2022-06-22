using System;
using System.Text;
using Avalonia.FreeDesktop;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Platform;
using Avalonia.Threading;
using NWayland.Interop;
using NWayland.Protocols.Wayland;

namespace Avalonia.Wayland
{
    internal class WlInputDevice : WlSeat.IEvents, WlPointer.IEvents, WlKeyboard.IEvents, WlTouch.IEvents, IDisposable
    {
        private readonly AvaloniaWaylandPlatform _platform;
        private readonly IPlatformThreadingInterface _platformThreading;
        private readonly ICursorFactory _cursorFactory;
        private readonly ITopLevelImpl _topLevelImpl;
        private readonly WlSurface _pointerSurface;

        private WlPointer? _wlPointer;
        private WlKeyboard? _wlKeyboard;
        private WlTouch? _wlTouch;
        private Point _pointerPosition;

        private IntPtr _xkbContext;
        private IntPtr _xkbKeymap;
        private IntPtr _xkbState;
        private IntPtr _xkbComposeState;

        private TimeSpan _repeatDelay;
        private TimeSpan _repeatInterval;
        private bool _firstRepeat;
        private uint _repeatTime;
        private uint _repeatCode;
        private XkbKey _repeatSym;
        private Key _repeatKey;
        private IDisposable? _timer;

        private int _ctrlMask;
        private int _altMask;
        private int _shiftMask;
        private int _metaMask;

        public WlInputDevice(AvaloniaWaylandPlatform platform, ITopLevelImpl topLevel)
        {
            _platform = platform;
            _platformThreading = AvaloniaLocator.Current.GetRequiredService<IPlatformThreadingInterface>();
            _cursorFactory = AvaloniaLocator.Current.GetRequiredService<ICursorFactory>();
            _topLevelImpl = topLevel;
            _platform.WlSeat.Events = this;
            _pointerSurface = platform.WlCompositor.CreateSurface();
        }

        public MouseDevice? MouseDevice { get; private set; }

        public IKeyboardDevice? KeyboardDevice { get; private set; }

        public TouchDevice? TouchDevice { get; private set; }

        public IInputRoot InputRoot { get; set; }

        public RawInputModifiers RawInputModifiers { get; private set; }

        public uint Serial { get; private set; }

        public uint PointerSurfaceSerial { get; private set; }

        public uint KeyboardEnterSerial { get; private set; }

        public void SetCursor(WlCursor? wlCursor)
        {
            wlCursor ??= _cursorFactory.GetCursor(StandardCursorType.Arrow) as WlCursor;
            if (_wlPointer is null || wlCursor is null || wlCursor.ImageCount <= 0)
                return;
            var cursorImage = wlCursor[0];
            if (cursorImage is null)
                return;
            _pointerSurface.Attach(cursorImage.WlBuffer, 0, 0);
            _pointerSurface.Damage(0, 0, cursorImage.Size.Width, cursorImage.Size.Height);
            _pointerSurface.Commit();
            _wlPointer.SetCursor(PointerSurfaceSerial, _pointerSurface, cursorImage.Hotspot.X, cursorImage.Hotspot.Y);
        }

        public void OnCapabilities(WlSeat eventSender, WlSeat.CapabilityEnum capabilities)
        {
            if (capabilities.HasAllFlags(WlSeat.CapabilityEnum.Pointer))
            {
                _wlPointer = _platform.WlSeat.GetPointer();
                _wlPointer.Events = this;
                MouseDevice = new MouseDevice();
            }
            if (capabilities.HasAllFlags(WlSeat.CapabilityEnum.Keyboard))
            {
                _xkbContext = LibXkbCommon.xkb_context_new(0);
                _wlKeyboard = _platform.WlSeat.GetKeyboard();
                _wlKeyboard.Events = this;
                KeyboardDevice = AvaloniaLocator.Current.GetService<IKeyboardDevice>();
            }
            if (capabilities.HasAllFlags(WlSeat.CapabilityEnum.Touch))
            {
                _wlTouch = _platform.WlSeat.GetTouch();
                _wlTouch.Events = this;
                TouchDevice = new TouchDevice();
            }
        }

        public void OnName(WlSeat eventSender, string name) { }

        public void OnEnter(WlPointer eventSender, uint serial, WlSurface surface, int surfaceX, int surfaceY)
        {
            PointerSurfaceSerial = serial;
            _pointerPosition = new Point(LibWayland.WlFixedToInt(surfaceX), LibWayland.WlFixedToInt(surfaceY));
            SetCursor(null);
        }

        public void OnLeave(WlPointer eventSender, uint serial, WlSurface surface)
        {
            PointerSurfaceSerial = serial;
            var args = new RawPointerEventArgs(MouseDevice!, 0, InputRoot, RawPointerEventType.LeaveWindow, _pointerPosition, RawInputModifiers);
            _topLevelImpl.Input?.Invoke(args);
        }

        public void OnMotion(WlPointer eventSender, uint time, int surfaceX, int surfaceY)
        {
            _pointerPosition = new Point(LibWayland.WlFixedToInt(surfaceX), LibWayland.WlFixedToInt(surfaceY));
            var args = new RawPointerEventArgs(MouseDevice!, time, InputRoot, RawPointerEventType.Move, _pointerPosition, RawInputModifiers);
            _topLevelImpl.Input?.Invoke(args);
        }

        public void OnButton(WlPointer eventSender, uint serial, uint time, uint button, WlPointer.ButtonStateEnum state)
        {
            Serial = serial;
            var type = button switch
            {
                (uint)EvKey.BTN_LEFT => state == WlPointer.ButtonStateEnum.Pressed ? RawPointerEventType.LeftButtonDown : RawPointerEventType.LeftButtonUp,
                (uint)EvKey.BTN_RIGHT => state == WlPointer.ButtonStateEnum.Pressed ? RawPointerEventType.RightButtonDown : RawPointerEventType.RightButtonUp,
                (uint)EvKey.BTN_MIDDLE => state == WlPointer.ButtonStateEnum.Pressed ? RawPointerEventType.MiddleButtonDown : RawPointerEventType.MiddleButtonUp
            };

            var args = new RawPointerEventArgs(MouseDevice!, time, InputRoot, type, _pointerPosition, RawInputModifiers);
            _topLevelImpl.Input?.Invoke(args);
        }

        public void OnAxis(WlPointer eventSender, uint time, WlPointer.AxisEnum axis, int value)
        {
            const double scrollFactor = 1.0 / 10.0;
            var scrollValue = -LibWayland.WlFixedToDouble(value) * scrollFactor;
            var delta = axis == WlPointer.AxisEnum.HorizontalScroll ? new Vector(scrollValue, 0) : new Vector(0, scrollValue);
            var args = new RawMouseWheelEventArgs(MouseDevice!, time, InputRoot, _pointerPosition, delta, RawInputModifiers);
            _topLevelImpl.Input?.Invoke(args);
        }

        public void OnFrame(WlPointer eventSender) { }

        public void OnAxisSource(WlPointer eventSender, WlPointer.AxisSourceEnum axisSource) { }

        public void OnAxisStop(WlPointer eventSender, uint time, WlPointer.AxisEnum axis) { }

        public void OnAxisDiscrete(WlPointer eventSender, WlPointer.AxisEnum axis, int discrete) { }

        public void OnKeymap(WlKeyboard eventSender, WlKeyboard.KeymapFormatEnum format, int fd, uint size)
        {
            var map = NativeMethods.mmap(IntPtr.Zero, new IntPtr(size), NativeMethods.PROT_READ, NativeMethods.MAP_PRIVATE, fd, IntPtr.Zero);
            if (map == new IntPtr(-1))
            {
                NativeMethods.close(fd);
                return;
            }

            var keymap = LibXkbCommon.xkb_keymap_new_from_string(_xkbContext, map, (uint)format, 0);
            NativeMethods.munmap(map, new IntPtr(fd));
            NativeMethods.close(fd);

            if (keymap == IntPtr.Zero)
                return;

            var state = LibXkbCommon.xkb_state_new(keymap);
            if (state == IntPtr.Zero)
            {
                LibXkbCommon.xkb_keymap_unref(keymap);
                return;
            }

            var locale = Environment.GetEnvironmentVariable("LC_ALL")
                         ?? Environment.GetEnvironmentVariable("LC_CTYPE")
                         ?? Environment.GetEnvironmentVariable("LANG")
                         ?? "C";

            var composeTable = LibXkbCommon.xkb_compose_table_new_from_locale(_xkbContext, locale, 0);
            if (composeTable != IntPtr.Zero)
            {
                var composeState = LibXkbCommon.xkb_compose_state_new(composeTable, 0);
                LibXkbCommon.xkb_compose_table_unref(composeTable);
                if (composeState != IntPtr.Zero)
                    _xkbComposeState = composeState;
            }

            LibXkbCommon.xkb_keymap_unref(_xkbKeymap);
            LibXkbCommon.xkb_state_unref(_xkbState);

            _xkbKeymap = keymap;
            _xkbState = state;

            _ctrlMask = 1 << (int)LibXkbCommon.xkb_keymap_mod_get_index(keymap, "Control");
            _altMask = 1 << (int)LibXkbCommon.xkb_keymap_mod_get_index(keymap, "Mod1");
            _shiftMask = 1 << (int)LibXkbCommon.xkb_keymap_mod_get_index(keymap, "Shift");
            _metaMask = 1 << (int)LibXkbCommon.xkb_keymap_mod_get_index(keymap, "Mod4");
        }

        public void OnEnter(WlKeyboard eventSender, uint serial, WlSurface surface, ReadOnlySpan<int> keys)
        {
            Serial = serial;
            KeyboardEnterSerial = serial;
        }

        public void OnLeave(WlKeyboard eventSender, uint serial, WlSurface surface)
        {
            Serial = serial;
        }

        public void OnKey(WlKeyboard eventSender, uint serial, uint time, uint key, WlKeyboard.KeyStateEnum state)
        {
            Serial = serial;
            var code = key + 8;
            var sym = LibXkbCommon.xkb_state_key_get_one_sym(_xkbState, code);
            var avaloniaKey = XkbKeyTransform.ConvertKey(sym);
            var eventType = state == WlKeyboard.KeyStateEnum.Pressed ? RawKeyEventType.KeyDown : RawKeyEventType.KeyUp;
            var keyEventArgs = new RawKeyEventArgs(KeyboardDevice!, time, InputRoot, eventType, avaloniaKey, RawInputModifiers);
            _topLevelImpl.Input?.Invoke(keyEventArgs);

            if (state == WlKeyboard.KeyStateEnum.Pressed)
            {
                var text = GetComposedString(sym, code);
                if (text is not null)
                {
                    var textEventArgs = new RawTextInputEventArgs(KeyboardDevice!, time, InputRoot, text);
                    _topLevelImpl.Input?.Invoke(textEventArgs);
                }

                if (LibXkbCommon.xkb_keymap_key_repeats(_xkbKeymap, code) && _repeatInterval > TimeSpan.Zero)
                {
                    _timer?.Dispose();
                    _firstRepeat = true;
                    _repeatTime = time;
                    _repeatCode = code;
                    _repeatSym = sym;
                    _repeatKey = avaloniaKey;
                    _timer = _platformThreading.StartTimer(DispatcherPriority.Input, _repeatDelay, OnRepeatKey);
                }
            }
            else if (_repeatKey == avaloniaKey)
            {
                _timer?.Dispose();
            }
        }

        public void OnModifiers(WlKeyboard eventSender, uint serial, uint modsDepressed, uint modsLatched, uint modsLocked, uint group)
        {
            Serial = serial;
            LibXkbCommon.xkb_state_update_mask(_xkbState, modsDepressed, modsLatched, modsLocked, 0, 0, group);
            var mask = LibXkbCommon.xkb_state_serialize_mods(_xkbState, LibXkbCommon.XkbStateComponent.XKB_STATE_MODS_EFFECTIVE);
            RawInputModifiers = RawInputModifiers.None;
            if ((mask & _ctrlMask) != 0)
                RawInputModifiers |= RawInputModifiers.Control;
            if ((mask & _altMask) != 0)
                RawInputModifiers |= RawInputModifiers.Alt;
            if ((mask & _shiftMask) != 0)
                RawInputModifiers |= RawInputModifiers.Shift;
            if ((mask & _metaMask) != 0)
                RawInputModifiers |= RawInputModifiers.Meta;
        }

        public void OnRepeatInfo(WlKeyboard eventSender, int rate, int delay)
        {
            _repeatDelay = new TimeSpan(delay * TimeSpan.TicksPerMillisecond);
            _repeatInterval = new TimeSpan(TimeSpan.TicksPerSecond / rate);
        }

        public void OnDown(WlTouch eventSender, uint serial, uint time, WlSurface surface, int id, int x, int y)
        {
            Serial = serial;
        }

        public void OnUp(WlTouch eventSender, uint serial, uint time, int id)
        {
            Serial = serial;
        }

        public void OnMotion(WlTouch eventSender, uint time, int id, int x, int y)
        {

        }

        public void OnFrame(WlTouch eventSender)
        {

        }

        public void OnCancel(WlTouch eventSender)
        {

        }

        public void OnShape(WlTouch eventSender, int id, int major, int minor)
        {

        }

        public void OnOrientation(WlTouch eventSender, int id, int orientation)
        {

        }

        private void OnRepeatKey()
        {
            _topLevelImpl.Input?.Invoke(new RawKeyEventArgs(KeyboardDevice!, _repeatTime, InputRoot, RawKeyEventType.KeyDown, _repeatKey, RawInputModifiers));
            var text = GetComposedString(_repeatSym, _repeatCode);
            if (text is not null)
                _topLevelImpl.Input?.Invoke( new RawTextInputEventArgs(KeyboardDevice!, _repeatTime, InputRoot, text));
            if (!_firstRepeat)
                return;
            _firstRepeat = false;
            _timer?.Dispose();
            _timer = _platformThreading.StartTimer(DispatcherPriority.Input, _repeatInterval, OnRepeatKey);
        }

        public void Dispose()
        {
            if (_xkbContext != IntPtr.Zero)
                LibXkbCommon.xkb_context_unref(_xkbContext);
            _timer?.Dispose();
            _wlPointer?.Dispose();
            _wlKeyboard?.Dispose();
            _wlTouch?.Dispose();
            MouseDevice?.Dispose();
            TouchDevice?.Dispose();
        }

        private unsafe string? GetComposedString(XkbKey sym, uint code)
        {
            LibXkbCommon.xkb_compose_state_feed(_xkbComposeState, sym);
            var status = LibXkbCommon.xkb_compose_state_get_status(_xkbComposeState);
            switch (status)
            {
                case LibXkbCommon.XkbComposeStatus.XKB_COMPOSE_COMPOSED:
                {
                    var size = LibXkbCommon.xkb_compose_state_get_utf8(_xkbComposeState, null, 0) + 1;
                    var buffer = stackalloc byte[size];
                    LibXkbCommon.xkb_compose_state_get_utf8(_xkbComposeState, buffer, size);
                    return Encoding.UTF8.GetString(buffer, size - 1);
                }
                case LibXkbCommon.XkbComposeStatus.XKB_COMPOSE_CANCELLED:
                {
                    LibXkbCommon.xkb_compose_state_reset(_xkbComposeState);
                    return null;
                }
                case LibXkbCommon.XkbComposeStatus.XKB_COMPOSE_NOTHING:
                {
                    var size = LibXkbCommon.xkb_state_key_get_utf8(_xkbState, code, null, 0) + 1;
                    var buffer = stackalloc byte[size];
                    LibXkbCommon.xkb_state_key_get_utf8(_xkbState, code, buffer, size);
                    var text = Encoding.UTF8.GetString(buffer, size - 1);
                    return text.Length == 1 && (text[0] < ' ' || text[0] == 0x7f) ? null : text; // Filer control codes or DEL
                }
                case LibXkbCommon.XkbComposeStatus.XKB_COMPOSE_COMPOSING:
                default:
                    return null;
            }
        }
    }
}
