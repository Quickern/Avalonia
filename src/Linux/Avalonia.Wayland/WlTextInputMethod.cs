using System;
using Avalonia.Input.Raw;
using Avalonia.Input.TextInput;
using NWayland.Protocols.TextInputUnstableV3;
using NWayland.Protocols.Wayland;

namespace Avalonia.Wayland
{
    internal class WlTextInputMethod : ITextInputMethodImpl, IDisposable, ZwpTextInputV3.IEvents
    {
        private readonly AvaloniaWaylandPlatform _platform;
        private readonly ZwpTextInputV3 _zwpTextInput;

        private ITextInputMethodClient? _client;

        public WlTextInputMethod(AvaloniaWaylandPlatform platform)
        {
            _platform = platform;
            _zwpTextInput = platform.ZwpTextInput!.GetTextInput(platform.WlSeat);
        }

        public void SetClient(ITextInputMethodClient? client)
        {
            _client = client;
            if (client is null)
                _zwpTextInput.Disable();
            else
                _zwpTextInput.Enable();
            _zwpTextInput.Commit();
        }

        public void SetCursorRect(Rect rect)
        {
            _zwpTextInput.SetCursorRectangle((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height);
            _zwpTextInput.Commit();
        }

        public void SetOptions(TextInputOptions options)
        {
            var contentHints = ParseContentHints(options);
            var contentPurpose = ParseContentPurpose(options.ContentType);
            _zwpTextInput.SetContentType(contentHints, contentPurpose);
            _zwpTextInput.Commit();
        }

        public void Reset()
        {
            _zwpTextInput.Disable();
            _zwpTextInput.Enable();
            _zwpTextInput.Commit();
        }

        public void OnEnter(ZwpTextInputV3 eventSender, WlSurface surface) => _platform.WlScreens.SetActiveSurface(surface);

        public void OnLeave(ZwpTextInputV3 eventSender, WlSurface surface) { }

        public void OnPreeditString(ZwpTextInputV3 eventSender, string? text, int cursorBegin, int cursorEnd)
        {
            if (_client?.SupportsPreedit is not true || text is null)
                return;
            _client?.SetPreeditText(text);
            _zwpTextInput.Commit();
        }

        public void OnCommitString(ZwpTextInputV3 eventSender, string? text)
        {
            var window = _platform.WlScreens.ActiveWindow;
            var keyboard = _platform.WlInputDevice.KeyboardDevice;
            if (window?.Input is null || window.InputRoot is null || keyboard is null || text is null)
                return;
            var args = new RawTextInputEventArgs(keyboard, 0, window.InputRoot, text);
            window.Input.Invoke(args);
        }

        public void OnDeleteSurroundingText(ZwpTextInputV3 eventSender, uint beforeLength, uint afterLength) { }

        public void OnDone(ZwpTextInputV3 eventSender, uint serial) { }

        public void Dispose()
        {
            _zwpTextInput.Dispose();
        }

        private static ZwpTextInputV3.ContentHintEnum ParseContentHints(TextInputOptions options)
        {
            var contentHints = ZwpTextInputV3.ContentHintEnum.None;
            if (options.Lowercase)
                contentHints |= ZwpTextInputV3.ContentHintEnum.Lowercase;
            if (options.Multiline)
                contentHints |= ZwpTextInputV3.ContentHintEnum.Multiline;
            if (options.Uppercase)
                contentHints |= ZwpTextInputV3.ContentHintEnum.Uppercase;
            if (options.AutoCapitalization)
                contentHints |= ZwpTextInputV3.ContentHintEnum.AutoCapitalization;
            if (options.IsSensitive)
                contentHints |= ZwpTextInputV3.ContentHintEnum.SensitiveData;
            return contentHints;
        }

        private static ZwpTextInputV3.ContentPurposeEnum ParseContentPurpose(TextInputContentType contentType) => contentType switch
        {
            TextInputContentType.Alpha => ZwpTextInputV3.ContentPurposeEnum.Alpha,
            TextInputContentType.Digits => ZwpTextInputV3.ContentPurposeEnum.Digits,
            TextInputContentType.Pin => ZwpTextInputV3.ContentPurposeEnum.Pin,
            TextInputContentType.Number => ZwpTextInputV3.ContentPurposeEnum.Number,
            TextInputContentType.Email => ZwpTextInputV3.ContentPurposeEnum.Email,
            TextInputContentType.Url => ZwpTextInputV3.ContentPurposeEnum.Url,
            TextInputContentType.Name => ZwpTextInputV3.ContentPurposeEnum.Name,
            TextInputContentType.Password => ZwpTextInputV3.ContentPurposeEnum.Password,
            _ => ZwpTextInputV3.ContentPurposeEnum.Normal
        };
    }
}