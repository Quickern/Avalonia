using System;
using System.Text;
using Avalonia.FreeDesktop;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Platform;
using Avalonia.Threading;
using NWayland.Interop;
using NWayland.Protocols.PointerGesturesUnstableV1;
using NWayland.Protocols.Wayland;

namespace Avalonia.Wayland
{
    internal class WlInputDevice : IDisposable, WlSeat.IEvents, WlPointer.IEvents, WlKeyboard.IEvents, WlTouch.IEvents, ZwpPointerGesturePinchV1.IEvents, ZwpPointerGestureSwipeV1.IEvents
    {
        private readonly AvaloniaWaylandPlatform _platform;
        private readonly IPlatformThreadingInterface _platformThreading;
        private readonly ICursorFactory _cursorFactory;
        private readonly WlSurface _pointerSurface;

        private WlPointer? _wlPointer;
        private WlKeyboard? _wlKeyboard;
        private WlTouch? _wlTouch;

        private ZwpPointerGesturePinchV1? _zwpPointerGesturePinch;
        private ZwpPointerGestureSwipeV1? _zwpPointerGestureSwipe;

        private Point _pointerPosition;
        private WlCursor? _currentCursor;
        private int _currentCursorImageIndex;
        private IDisposable? _pointerTimer;

        private int _touchId;
        private Point _touchPosition;

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
        private IDisposable? _keyboardTimer;

        private int _ctrlMask;
        private int _altMask;
        private int _shiftMask;
        private int _metaMask;

        public WlInputDevice(AvaloniaWaylandPlatform platform)
        {
            _platform = platform;
            _platformThreading = AvaloniaLocator.Current.GetRequiredService<IPlatformThreadingInterface>();
            _cursorFactory = AvaloniaLocator.Current.GetRequiredService<ICursorFactory>();
            _platform.WlSeat.Events = this;
            _pointerSurface = platform.WlCompositor.CreateSurface();
        }

        public MouseDevice? MouseDevice { get; private set; }

        public IKeyboardDevice? KeyboardDevice { get; private set; }

        public TouchDevice? TouchDevice { get; private set; }

        public RawInputModifiers RawInputModifiers { get; private set; }

        public uint Serial { get; private set; }

        public uint PointerSurfaceSerial { get; private set; }

        public uint KeyboardEnterSerial { get; private set; }

        public void SetCursor(WlCursor? wlCursor)
        {
            wlCursor ??= _cursorFactory.GetCursor(StandardCursorType.Arrow) as WlCursor;
            if (_wlPointer is null || wlCursor is null || wlCursor.ImageCount <= 0  || _currentCursor == wlCursor)
                return;
            _pointerTimer?.Dispose();
            _currentCursor = wlCursor;
            _currentCursorImageIndex = -1;
            if (wlCursor.ImageCount == 1)
                SetCursorImage(wlCursor[0]);
            else
                _pointerTimer = _platformThreading.StartTimer(DispatcherPriority.Render, wlCursor[0].Delay, OnCursorAnimation);
        }

        public void OnCapabilities(WlSeat eventSender, WlSeat.CapabilityEnum capabilities)
        {
            if (capabilities.HasAllFlags(WlSeat.CapabilityEnum.Pointer))
            {
                _wlPointer = _platform.WlSeat.GetPointer();
                _wlPointer.Events = this;
                MouseDevice = new MouseDevice();
                if (_platform.ZwpPointerGestures is null)
                    return;
                _zwpPointerGesturePinch = _platform.ZwpPointerGestures.GetPinchGesture(_wlPointer);
                _zwpPointerGestureSwipe = _platform.ZwpPointerGestures.GetSwipeGesture(_wlPointer);
                _zwpPointerGesturePinch.Events = this;
                _zwpPointerGestureSwipe.Events = this;
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

        public void OnEnter(WlPointer eventSender, uint serial, WlSurface surface, WlFixed surfaceX, WlFixed surfaceY)
        {
            _platform.WlScreens.SetActiveSurface(surface);
            var window = _platform.WlScreens.ActiveWindow;
            if (window?.InputRoot is null)
                return;
            PointerSurfaceSerial = serial;
            _pointerPosition = new Point((int)surfaceX, (int)surfaceY);
            var args = new RawPointerEventArgs(MouseDevice!, 0, window.InputRoot, RawPointerEventType.Move, _pointerPosition, RawInputModifiers);
            window.Input?.Invoke(args);
        }

        public void OnLeave(WlPointer eventSender, uint serial, WlSurface surface)
        {
            var window = _platform.WlScreens.ActiveWindow;
            if (window?.InputRoot is null)
                return;
            PointerSurfaceSerial = serial;
            var args = new RawPointerEventArgs(MouseDevice!, 0, window.InputRoot, RawPointerEventType.LeaveWindow, _pointerPosition, RawInputModifiers);
            window.Input?.Invoke(args);
        }

        public void OnMotion(WlPointer eventSender, uint time, WlFixed surfaceX, WlFixed surfaceY)
        {
            var window = _platform.WlScreens.ActiveWindow;
            if (window?.InputRoot is null)
                return;
            _pointerPosition = new Point((int)surfaceX, (int)surfaceY);
            var args = new RawPointerEventArgs(MouseDevice!, time, window.InputRoot, RawPointerEventType.Move, _pointerPosition, RawInputModifiers);
            window.Input?.Invoke(args);
        }

        public void OnButton(WlPointer eventSender, uint serial, uint time, uint button, WlPointer.ButtonStateEnum state)
        {
            var window = _platform.WlScreens.ActiveWindow;
            if (window?.InputRoot is null)
                return;
            Serial = serial;
            RawPointerEventType type;
            switch (button)
            {
                case (uint)EvKey.BTN_LEFT when state is WlPointer.ButtonStateEnum.Pressed:
                    type = RawPointerEventType.LeftButtonDown;
                    RawInputModifiers |= RawInputModifiers.LeftMouseButton;
                    break;
                case (uint)EvKey.BTN_LEFT when state is WlPointer.ButtonStateEnum.Released:
                    type = RawPointerEventType.LeftButtonUp;
                    RawInputModifiers &= ~RawInputModifiers.LeftMouseButton;
                    break;
                case (uint)EvKey.BTN_RIGHT when state is WlPointer.ButtonStateEnum.Pressed:
                    type = RawPointerEventType.RightButtonDown;
                    RawInputModifiers |= RawInputModifiers.RightMouseButton;
                    break;
                case (uint)EvKey.BTN_RIGHT when state is WlPointer.ButtonStateEnum.Released:
                    type = RawPointerEventType.RightButtonUp;
                    RawInputModifiers &= ~RawInputModifiers.RightMouseButton;
                    break;
                case (uint)EvKey.BTN_MIDDLE when state is WlPointer.ButtonStateEnum.Pressed:
                    type = RawPointerEventType.MiddleButtonDown;
                    RawInputModifiers |= RawInputModifiers.MiddleMouseButton;
                    break;
                case (uint)EvKey.BTN_MIDDLE when state is WlPointer.ButtonStateEnum.Released:
                    type = RawPointerEventType.MiddleButtonUp;
                    RawInputModifiers &= ~RawInputModifiers.MiddleMouseButton;
                    break;
                case (uint)EvKey.BTN_SIDE when state is WlPointer.ButtonStateEnum.Pressed:
                    type = RawPointerEventType.XButton2Down;
                    RawInputModifiers |= RawInputModifiers.XButton2MouseButton;
                    break;
                case (uint)EvKey.BTN_SIDE when state is WlPointer.ButtonStateEnum.Released:
                    type = RawPointerEventType.XButton2Up;
                    RawInputModifiers &= ~RawInputModifiers.XButton2MouseButton;
                    break;
                case (uint)EvKey.BTN_EXTRA when state is WlPointer.ButtonStateEnum.Pressed:
                    type = RawPointerEventType.XButton1Down;
                    RawInputModifiers |= RawInputModifiers.XButton1MouseButton;
                    break;
                case (uint)EvKey.BTN_EXTRA when state is WlPointer.ButtonStateEnum.Released:
                    type = RawPointerEventType.XButton1Up;
                    RawInputModifiers &= ~RawInputModifiers.XButton1MouseButton;
                    break;
                default:
                    return;
            }

            var args = new RawPointerEventArgs(MouseDevice!, time, window.InputRoot, type, _pointerPosition, RawInputModifiers);
            window.Input?.Invoke(args);
        }

        public void OnAxis(WlPointer eventSender, uint time, WlPointer.AxisEnum axis, WlFixed value)
        {
            if (_platform.WlScreens.ActiveWindow?.InputRoot is null)
                return;
            const double scrollFactor = 0.1;
            var scrollValue = -(double)value * scrollFactor;
            var delta = axis == WlPointer.AxisEnum.HorizontalScroll ? new Vector(scrollValue, 0) : new Vector(0, scrollValue);
            var args = new RawMouseWheelEventArgs(MouseDevice!, time, _platform.WlScreens.ActiveWindow.InputRoot, _pointerPosition, delta, RawInputModifiers);
            _platform.WlScreens.ActiveWindow.Input?.Invoke(args);
        }

        public void OnFrame(WlPointer eventSender) { }

        public void OnAxisSource(WlPointer eventSender, WlPointer.AxisSourceEnum axisSource) { }

        public void OnAxisStop(WlPointer eventSender, uint time, WlPointer.AxisEnum axis) { }

        public void OnAxisDiscrete(WlPointer eventSender, WlPointer.AxisEnum axis, int discrete) { }

        public void OnAxisValue120(WlPointer eventSender, WlPointer.AxisEnum axis, int value120) { }

        public void OnKeymap(WlKeyboard eventSender, WlKeyboard.KeymapFormatEnum format, int fd, uint size)
        {
            var map = LibC.mmap(IntPtr.Zero, new IntPtr(size), MemoryProtection.PROT_READ, SharingType.MAP_PRIVATE, fd, IntPtr.Zero);
            if (map == new IntPtr(-1))
            {
                LibC.close(fd);
                return;
            }

            var keymap = LibXkbCommon.xkb_keymap_new_from_string(_xkbContext, map, (uint)format, 0);
            LibC.munmap(map, new IntPtr(size));
            LibC.close(fd);

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
            _platform.WlScreens.SetActiveSurface(surface);
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
            if (_platform.WlScreens.ActiveWindow?.InputRoot is null)
                return;
            var code = key + 8;
            var sym = LibXkbCommon.xkb_state_key_get_one_sym(_xkbState, code);
            var avaloniaKey = XkbKeyTransform.ConvertKey(sym);
            var eventType = state == WlKeyboard.KeyStateEnum.Pressed ? RawKeyEventType.KeyDown : RawKeyEventType.KeyUp;
            var keyEventArgs = new RawKeyEventArgs(KeyboardDevice!, time, _platform.WlScreens.ActiveWindow.InputRoot, eventType, avaloniaKey, RawInputModifiers);
            _platform.WlScreens.ActiveWindow.Input?.Invoke(keyEventArgs);

            if (state == WlKeyboard.KeyStateEnum.Pressed)
            {
                var text = GetComposedString(sym, code);
                if (text is not null)
                {
                    var textEventArgs = new RawTextInputEventArgs(KeyboardDevice!, time, _platform.WlScreens.ActiveWindow.InputRoot, text);
                    _platform.WlScreens.ActiveWindow.Input?.Invoke(textEventArgs);
                }

                if (LibXkbCommon.xkb_keymap_key_repeats(_xkbKeymap, code) && _repeatInterval > TimeSpan.Zero)
                {
                    _keyboardTimer?.Dispose();
                    _firstRepeat = true;
                    _repeatTime = time;
                    _repeatCode = code;
                    _repeatSym = sym;
                    _repeatKey = avaloniaKey;
                    _keyboardTimer = _platformThreading.StartTimer(DispatcherPriority.Input, _repeatDelay, OnRepeatKey);
                }
            }
            else if (_repeatKey == avaloniaKey)
            {
                _keyboardTimer?.Dispose();
            }
        }

        public void OnModifiers(WlKeyboard eventSender, uint serial, uint modsDepressed, uint modsLatched, uint modsLocked, uint group)
        {
            Serial = serial;
            LibXkbCommon.xkb_state_update_mask(_xkbState, modsDepressed, modsLatched, modsLocked, 0, 0, group);
            var mask = LibXkbCommon.xkb_state_serialize_mods(_xkbState, LibXkbCommon.XkbStateComponent.XKB_STATE_MODS_EFFECTIVE);
            if ((mask & _ctrlMask) != 0)
                RawInputModifiers |= RawInputModifiers.Control;
            else
                RawInputModifiers &= ~RawInputModifiers.Control;
            if ((mask & _altMask) != 0)
                RawInputModifiers |= RawInputModifiers.Alt;
            else
                RawInputModifiers &= ~RawInputModifiers.Alt;
            if ((mask & _shiftMask) != 0)
                RawInputModifiers |= RawInputModifiers.Shift;
            else
                RawInputModifiers &= ~RawInputModifiers.Shift;
            if ((mask & _metaMask) != 0)
                RawInputModifiers |= RawInputModifiers.Meta;
            else
                RawInputModifiers &= ~RawInputModifiers.Meta;
        }

        public void OnRepeatInfo(WlKeyboard eventSender, int rate, int delay)
        {
            _repeatDelay = TimeSpan.FromMilliseconds(delay);
            _repeatInterval = TimeSpan.FromSeconds(1f / rate);
        }

        public void OnDown(WlTouch eventSender, uint serial, uint time, WlSurface surface, int id, WlFixed x, WlFixed y)
        {
            Serial = serial;
            _touchId = id;
            _platform.WlScreens.SetActiveSurface(surface);
            var window = _platform.WlScreens.ActiveWindow;
            if (window?.Input is null || window.InputRoot is null)
                return;
            _touchPosition = new Point((double)x, (double)y);
            var args = new RawTouchEventArgs(TouchDevice!, time, window.InputRoot, RawPointerEventType.TouchBegin, _touchPosition, RawInputModifiers, id);
            window.Input.Invoke(args);
        }

        public void OnUp(WlTouch eventSender, uint serial, uint time, int id)
        {
            Serial = serial;
            _touchId = id;
            var window = _platform.WlScreens.ActiveWindow;
            if (window?.Input is null || window.InputRoot is null)
                return;
            var args = new RawTouchEventArgs(TouchDevice!, time, window.InputRoot, RawPointerEventType.TouchEnd, _touchPosition, RawInputModifiers, id);
            window.Input.Invoke(args);
        }

        public void OnMotion(WlTouch eventSender, uint time, int id, WlFixed x, WlFixed y)
        {
            _touchId = id;
            var window = _platform.WlScreens.ActiveWindow;
            if (window?.Input is null || window.InputRoot is null)
                return;
            _touchPosition = new Point((double)x, (double)y);
            var args = new RawTouchEventArgs(TouchDevice!, time, window.InputRoot, RawPointerEventType.TouchUpdate, _touchPosition, RawInputModifiers, id);
            window.Input.Invoke(args);
        }

        public void OnFrame(WlTouch eventSender) { }

        public void OnCancel(WlTouch eventSender)
        {
            var window = _platform.WlScreens.ActiveWindow;
            if (window?.Input is null || window.InputRoot is null || _touchId == 0)
                return;
            var args = new RawTouchEventArgs(TouchDevice!, 0, window.InputRoot, RawPointerEventType.TouchCancel, _touchPosition, RawInputModifiers, _touchId);
            window.Input.Invoke(args);
            _touchId = 0;
        }

        public void OnShape(WlTouch eventSender, int id, WlFixed major, WlFixed minor) { }

        public void OnOrientation(WlTouch eventSender, int id, WlFixed orientation) { }

        public void OnBegin(ZwpPointerGesturePinchV1 eventSender, uint serial, uint time, WlSurface surface, uint fingers)
        {
            Serial = serial;
            _platform.WlScreens.SetActiveSurface(surface);
        }

        public void OnUpdate(ZwpPointerGesturePinchV1 eventSender, uint time, WlFixed dx, WlFixed dy, WlFixed scale, WlFixed rotation)
        {
            var window = _platform.WlScreens.ActiveWindow;
            if (window?.Input is null || window.InputRoot is null)
                return;

            var deltaMagnify = new Vector((double)dx, (double)dy);
            if (deltaMagnify != Vector.Zero)
            {
                var magnifyArgs = new RawPointerGestureEventArgs(MouseDevice!, time, window.InputRoot, RawPointerEventType.Magnify, _pointerPosition, deltaMagnify, RawInputModifiers);
                window.Input.Invoke(magnifyArgs);
            }

            var rad = Math.PI / 180 * (double)rotation;
            if (rad != 0)
            {
                var deltaRotation = new Vector(Math.Cos(rad), Math.Sin(rad));
                var rotateArgs = new RawPointerGestureEventArgs(MouseDevice!, time, window.InputRoot, RawPointerEventType.Rotate, _pointerPosition, deltaRotation, RawInputModifiers);
                window.Input.Invoke(rotateArgs);
            }
        }

        public void OnEnd(ZwpPointerGesturePinchV1 eventSender, uint serial, uint time, int cancelled)
        {
            Serial = serial;
        }

        public void OnBegin(ZwpPointerGestureSwipeV1 eventSender, uint serial, uint time, WlSurface surface, uint fingers)
        {
            Serial = serial;
            _platform.WlScreens.SetActiveSurface(surface);
        }

        public void OnUpdate(ZwpPointerGestureSwipeV1 eventSender, uint time, WlFixed dx, WlFixed dy)
        {
            var window = _platform.WlScreens.ActiveWindow;
            if (window?.Input is null || window.InputRoot is null)
                return;
            var deltaSwipe = new Vector((double)dx, (double)dy);
            var args = new RawPointerGestureEventArgs(MouseDevice!, time, window.InputRoot, RawPointerEventType.Swipe, _pointerPosition, deltaSwipe, RawInputModifiers);
            window.Input.Invoke(args);
        }

        public void OnEnd(ZwpPointerGestureSwipeV1 eventSender, uint serial, uint time, int cancelled)
        {
            Serial = serial;
        }

        public void Dispose()
        {
            if (_xkbContext != IntPtr.Zero)
                LibXkbCommon.xkb_context_unref(_xkbContext);
            _zwpPointerGesturePinch?.Dispose();
            _zwpPointerGestureSwipe?.Dispose();
            _keyboardTimer?.Dispose();
            _wlPointer?.Dispose();
            _wlKeyboard?.Dispose();
            _wlTouch?.Dispose();
            MouseDevice?.Dispose();
            TouchDevice?.Dispose();
        }

        private void OnRepeatKey()
        {
            var window = _platform.WlScreens.ActiveWindow;
            if (window?.InputRoot is null)
                return;
            window.Input?.Invoke(new RawKeyEventArgs(KeyboardDevice!, _repeatTime, window.InputRoot, RawKeyEventType.KeyDown, _repeatKey, RawInputModifiers));
            var text = GetComposedString(_repeatSym, _repeatCode);
            if (text is not null)
                window.Input?.Invoke( new RawTextInputEventArgs(KeyboardDevice!, _repeatTime, window.InputRoot, text));
            if (!_firstRepeat)
                return;
            _firstRepeat = false;
            _keyboardTimer?.Dispose();
            _keyboardTimer = _platformThreading.StartTimer(DispatcherPriority.Input, _repeatInterval, OnRepeatKey);
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

        private void OnCursorAnimation()
        {
            var oldImage = _currentCursorImageIndex == -1 ? null : _currentCursor![_currentCursorImageIndex];
            if (++_currentCursorImageIndex >= _currentCursor!.ImageCount)
                _currentCursorImageIndex = 0;
            var newImage = _currentCursor[_currentCursorImageIndex];
            SetCursorImage(newImage);
            if (oldImage is null || oldImage.Delay == newImage.Delay)
                return;
            _pointerTimer?.Dispose();
            _pointerTimer = _platformThreading.StartTimer(DispatcherPriority.Render, newImage.Delay, OnCursorAnimation);
        }

        private void SetCursorImage(WlCursor.WlCursorImage cursorImage)
        {
            _pointerSurface.Attach(cursorImage.WlBuffer, 0, 0);
            _pointerSurface.DamageBuffer(0, 0, cursorImage.Size.Width, cursorImage.Size.Height);
            _pointerSurface.Commit();
            _wlPointer!.SetCursor(PointerSurfaceSerial, _pointerSurface, cursorImage.Hotspot.X, cursorImage.Hotspot.Y);
        }
    }
}
