using System;
using System.Timers;
using Avalonia.FreeDesktop;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Platform;
using Avalonia.Threading;
using NWayland.Protocols.Wayland;

namespace Avalonia.Wayland
{
    public class WlInputDevice : WlSeat.IEvents, WlPointer.IEvents, WlKeyboard.IEvents, WlTouch.IEvents, IDisposable
    {
        private readonly AvaloniaWaylandPlatform _platform;
        private readonly ITopLevelImpl _topLevelImpl;
        private readonly Timer _timer;

        private WlPointer? _wlPointer;
        private WlKeyboard? _wlKeyboard;
        private WlTouch? _wlTouch;
        private Point _pointerPosition;

        private IntPtr _xkbKeymap;
        private IntPtr _xkbState;

        private int _repeatDelay;
        private int _repeatInterval;
        private uint _repeatTime;
        private uint _repeatSym;
        private bool _firstRepeat;

        private Key _currentKey;
        private string? _currentText;

        private int _ctrlMask;
        private int _altMask;
        private int _shiftMask;
        private int _metaMask;
        private RawInputModifiers _modifiers;

        public WlInputDevice(AvaloniaWaylandPlatform platform, ITopLevelImpl topLevel)
        {
            _platform = platform;
            _topLevelImpl = topLevel;
            _platform.WlSeat.Events = this;
            _timer = new Timer();
            _timer.Elapsed += OnRepeatKey;
        }

        public MouseDevice? MouseDevice { get; private set; }

        public IKeyboardDevice? KeyboardDevice { get; private set; }

        public TouchDevice? TouchDevice { get; private set; }

        public IInputRoot InputRoot { get; set; }

        public uint LastSerial { get; private set; }

        public uint LastPointerSurfaceSerial { get; private set; }

        public void SetCursor(WlCursorFactory.WlCursor wlCursor)
        {
            if (_wlPointer is null) return;
            //_wlPointer.SetCursor(LastSerial, );
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
            LastPointerSurfaceSerial = serial;
            _pointerPosition = new Point(surfaceX / 256d, surfaceY / 256d);
        }

        public void OnLeave(WlPointer eventSender, uint serial, WlSurface surface)
        {
            LastPointerSurfaceSerial = serial;
            _topLevelImpl.Input?.Invoke(new RawPointerEventArgs(MouseDevice!, 0, InputRoot, RawPointerEventType.LeaveWindow, _pointerPosition, _modifiers));
        }

        public void OnMotion(WlPointer eventSender, uint time, int surfaceX, int surfaceY)
        {
            _pointerPosition = new Point(surfaceX / 256d, surfaceY / 256d);
            _topLevelImpl.Input?.Invoke(new RawPointerEventArgs(MouseDevice!, time, InputRoot, RawPointerEventType.Move, _pointerPosition, _modifiers));
        }

        public void OnButton(WlPointer eventSender, uint serial, uint time, uint button, WlPointer.ButtonStateEnum state)
        {
            LastSerial = serial;
            _topLevelImpl.Input?.Invoke(new RawPointerEventArgs(MouseDevice!, time, InputRoot, ProcessButton(button, state), _pointerPosition, _modifiers));
        }

        public void OnAxis(WlPointer eventSender, uint time, WlPointer.AxisEnum axis, int value)
            => _topLevelImpl.Input?.Invoke(new RawMouseWheelEventArgs(MouseDevice!, time, InputRoot, _pointerPosition, GetVectorForAxis(axis, value / 512d), _modifiers));

        public void OnFrame(WlPointer eventSender) { }

        public void OnAxisSource(WlPointer eventSender, WlPointer.AxisSourceEnum axisSource) { }

        public void OnAxisStop(WlPointer eventSender, uint time, WlPointer.AxisEnum axis) { }

        public void OnAxisDiscrete(WlPointer eventSender, WlPointer.AxisEnum axis, int discrete) { }

        public void OnAxisValue120(WlPointer eventSender, WlPointer.AxisEnum axis, int value120) { }

        public void OnKeymap(WlKeyboard eventSender, WlKeyboard.KeymapFormatEnum format, int fd, uint size)
        {
            var map = NativeMethods.mmap(IntPtr.Zero, new IntPtr(size), 0x1, 0x02, fd, IntPtr.Zero);

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
            LastSerial = serial;
        }

        public void OnLeave(WlKeyboard eventSender, uint serial, WlSurface surface)
        {
            LastSerial = serial;
        }

        public unsafe void OnKey(WlKeyboard eventSender, uint serial, uint time, uint key, WlKeyboard.KeyStateEnum state)
        {
            LastSerial = serial;
            var code = key + 8;
            uint* syms;
            var numSyms = LibXkbCommon.xkb_state_key_get_syms(_xkbState, code, &syms);
            var sym = numSyms == 1 ? syms[0] : 0;
            if (sym == 0)
                return;
            _currentKey = XkbKeyTransform.ConvertKey((XkbKey)sym);
            _topLevelImpl.Input?.Invoke(new RawKeyEventArgs(KeyboardDevice!, time, InputRoot, ProcessKeyState(state), _currentKey, _modifiers));
            if (state == WlKeyboard.KeyStateEnum.Released && _repeatSym == sym)
            {
                _timer.Stop();
                _currentText = null;
            }
            else if (state == WlKeyboard.KeyStateEnum.Pressed)
            {
                var chars = stackalloc char[16];
                if (LibXkbCommon.xkb_keysym_to_utf8(sym, chars, sizeof(char) * 16) > 0)
                {
                    _currentText = new string(chars);
                    _topLevelImpl.Input?.Invoke(new RawTextInputEventArgs(KeyboardDevice!, time, InputRoot, _currentText));
                }
                if (!LibXkbCommon.xkb_keymap_key_repeats(_xkbKeymap, code))
                    return;
                _repeatSym = sym;
                _repeatTime = time;
                _firstRepeat = true;
                _timer.Interval = _repeatDelay;
                _timer.Start();
            }
        }

        public void OnModifiers(WlKeyboard eventSender, uint serial, uint modsDepressed, uint modsLatched, uint modsLocked, uint group)
        {
            LastSerial = serial;
            LibXkbCommon.xkb_state_update_mask(_xkbState, modsDepressed, modsLatched, modsLocked, 0, 0, group);
            var mask = LibXkbCommon.xkb_state_serialize_mods(_xkbState, LibXkbCommon.XkbStateComponent.XKB_STATE_MODS_EFFECTIVE);
            _modifiers = RawInputModifiers.None;
            if ((mask & _ctrlMask) != 0)
                _modifiers |= RawInputModifiers.Control;
            if ((mask & _altMask) != 0)
                _modifiers |= RawInputModifiers.Alt;
            if ((mask & _shiftMask) != 0)
                _modifiers |= RawInputModifiers.Shift;
            if ((mask & _metaMask) != 0)
                _modifiers |= RawInputModifiers.Meta;
        }

        public void OnRepeatInfo(WlKeyboard eventSender, int rate, int delay)
        {
            _repeatDelay = delay;
            _repeatInterval = rate;
        }

        public void OnDown(WlTouch eventSender, uint serial, uint time, WlSurface surface, int id, int x, int y)
        {
            LastSerial = serial;
        }

        public void OnUp(WlTouch eventSender, uint serial, uint time, int id)
        {
            LastSerial = serial;
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

        private void OnRepeatKey(object o, ElapsedEventArgs args)
        {
            if (_topLevelImpl.Input is null)
                return;
            Dispatcher.UIThread.Post(_topLevelImpl.Input.Invoke, new RawKeyEventArgs(KeyboardDevice!, _repeatTime, InputRoot, RawKeyEventType.KeyDown, _currentKey, _modifiers));
            if (_currentText is null)
                return;
            Dispatcher.UIThread.Post(_topLevelImpl.Input.Invoke, new RawTextInputEventArgs(KeyboardDevice!, _repeatTime, InputRoot, _currentText));
            if (!_firstRepeat)
                return;
            _firstRepeat = false;
            _timer.Interval = _repeatInterval;
        }

        private static RawPointerEventType ProcessButton(uint button, WlPointer.ButtonStateEnum buttonState)
            => button switch
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

        private static RawKeyEventType ProcessKeyState(WlKeyboard.KeyStateEnum keyState)
            => keyState switch
            {
                WlKeyboard.KeyStateEnum.Pressed => RawKeyEventType.KeyDown,
                WlKeyboard.KeyStateEnum.Released => RawKeyEventType.KeyUp
            };

        private static Vector GetVectorForAxis(WlPointer.AxisEnum axis, double value)
            => axis == WlPointer.AxisEnum.HorizontalScroll ? new Vector(-value, 0) : new Vector(0, -value);

        public void Dispose()
        {
            _timer.Dispose();
            _wlPointer?.Dispose();
            _wlKeyboard?.Dispose();
            _wlTouch?.Dispose();
            MouseDevice?.Dispose();
            TouchDevice?.Dispose();
        }
    }
}
