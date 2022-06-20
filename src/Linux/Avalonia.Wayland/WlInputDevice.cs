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

        private IntPtr _xkbKeymap;
        private IntPtr _xkbState;

        private TimeSpan _repeatDelay;
        private TimeSpan _repeatInterval;
        private uint _repeatTime;
        private uint _repeatCode;
        private bool _firstRepeat;
        private IDisposable? _timer;

        private Key _currentKey;
        private string? _currentText;

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
            _topLevelImpl.Input?.Invoke(new RawPointerEventArgs(MouseDevice!, 0, InputRoot, RawPointerEventType.LeaveWindow, _pointerPosition, RawInputModifiers));
        }

        public void OnMotion(WlPointer eventSender, uint time, int surfaceX, int surfaceY)
        {
            _pointerPosition = new Point(LibWayland.WlFixedToInt(surfaceX), LibWayland.WlFixedToInt(surfaceY));
            _topLevelImpl.Input?.Invoke(new RawPointerEventArgs(MouseDevice!, time, InputRoot, RawPointerEventType.Move, _pointerPosition, RawInputModifiers));
        }

        public void OnButton(WlPointer eventSender, uint serial, uint time, uint button, WlPointer.ButtonStateEnum state)
        {
            Serial = serial;
            _topLevelImpl.Input?.Invoke(new RawPointerEventArgs(MouseDevice!, time, InputRoot, ProcessButton(button, state), _pointerPosition, RawInputModifiers));
        }

        public void OnAxis(WlPointer eventSender, uint time, WlPointer.AxisEnum axis, int value)
            => _topLevelImpl.Input?.Invoke(new RawMouseWheelEventArgs(MouseDevice!, time, InputRoot, _pointerPosition, GetVectorForAxis(axis, LibWayland.WlFixedToInt(value)), RawInputModifiers));

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

            var context = LibXkbCommon.xkb_context_new(0);
            var keymap = LibXkbCommon.xkb_keymap_new_from_string(context, map, (uint)format, 0);
            NativeMethods.munmap(map, new IntPtr(fd));
            NativeMethods.close(fd);

            if (keymap == IntPtr.Zero)
            {
                LibXkbCommon.xkb_context_unref(context);
                return;
            }

            LibXkbCommon.xkb_keymap_unref(_xkbKeymap);
            _xkbKeymap = keymap;
            LibXkbCommon.xkb_state_unref(_xkbState);
            _xkbState = LibXkbCommon.xkb_state_new(keymap);
            LibXkbCommon.xkb_context_unref(context);

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

        public unsafe void OnKey(WlKeyboard eventSender, uint serial, uint time, uint key, WlKeyboard.KeyStateEnum state)
        {
            Serial = serial;
            var code = key + 8;
            var sym = LibXkbCommon.xkb_state_key_get_one_sym(_xkbState, code);
            _currentKey = XkbKeyTransform.ConvertKey((XkbKey)sym);
            _topLevelImpl.Input?.Invoke(new RawKeyEventArgs(KeyboardDevice!, time, InputRoot, KeyStateToRawKeyEventType(state), _currentKey, RawInputModifiers));
            if (state == WlKeyboard.KeyStateEnum.Released && _repeatCode == code)
            {
                _timer?.Dispose();
                _timer = null;
                _currentText = null;
            }
            else if (state == WlKeyboard.KeyStateEnum.Pressed)
            {
                var chars = stackalloc byte[16];
                var count = LibXkbCommon.xkb_state_key_get_utf8(_xkbState, code, (IntPtr)chars, 16);
                _currentText = Encoding.UTF8.GetString(chars, count);
                _topLevelImpl.Input?.Invoke(new RawTextInputEventArgs(KeyboardDevice!, time, InputRoot, _currentText));
                if (!LibXkbCommon.xkb_keymap_key_repeats(_xkbKeymap, code))
                    return;
                _repeatCode = code;
                _repeatTime = time;
                _firstRepeat = true;
                _timer = _platformThreading.StartTimer(DispatcherPriority.Input, _repeatDelay, OnRepeatKey);
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
            _topLevelImpl.Input?.Invoke(new RawKeyEventArgs(KeyboardDevice!, _repeatTime, InputRoot, RawKeyEventType.KeyDown, _currentKey, RawInputModifiers));
            if (_currentText is null)
                return;
            _topLevelImpl.Input?.Invoke( new RawTextInputEventArgs(KeyboardDevice!, _repeatTime, InputRoot, _currentText));
            if (!_firstRepeat)
                return;
            _firstRepeat = false;
            _timer!.Dispose();
            _timer = _platformThreading.StartTimer(DispatcherPriority.Input, _repeatInterval, OnRepeatKey);
        }

        private static RawPointerEventType ProcessButton(uint button, WlPointer.ButtonStateEnum buttonState) => button switch
            {
                (uint)EvKey.BTN_LEFT => buttonState == WlPointer.ButtonStateEnum.Pressed ?
                    RawPointerEventType.LeftButtonDown :
                    RawPointerEventType.LeftButtonUp,
                (uint)EvKey.BTN_RIGHT => buttonState == WlPointer.ButtonStateEnum.Pressed ?
                    RawPointerEventType.RightButtonDown :
                    RawPointerEventType.RightButtonUp,
                (uint)EvKey.BTN_MIDDLE => buttonState == WlPointer.ButtonStateEnum.Pressed ?
                    RawPointerEventType.MiddleButtonDown :
                    RawPointerEventType.MiddleButtonUp,
                _ => RawPointerEventType.NonClientLeftButtonDown
            };

        private static RawKeyEventType KeyStateToRawKeyEventType(WlKeyboard.KeyStateEnum keyState) => keyState switch
            {
                WlKeyboard.KeyStateEnum.Pressed => RawKeyEventType.KeyDown,
                WlKeyboard.KeyStateEnum.Released => RawKeyEventType.KeyUp
            };

        private static Vector GetVectorForAxis(WlPointer.AxisEnum axis, double value)
            => axis == WlPointer.AxisEnum.HorizontalScroll ? new Vector(-value, 0) : new Vector(0, -value);

        public void Dispose()
        {
            _timer?.Dispose();
            _wlPointer?.Dispose();
            _wlKeyboard?.Dispose();
            _wlTouch?.Dispose();
            MouseDevice?.Dispose();
            TouchDevice?.Dispose();
        }
    }
}
