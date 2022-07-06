using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.FreeDesktop;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Input.Raw;
using NWayland.Interop;
using NWayland.Protocols.Wayland;

namespace Avalonia.Wayland
{
    internal class WlDataHandler : IClipboard, IPlatformDragSource, IDisposable
    {
        private readonly AvaloniaWaylandPlatform _platform;
        private readonly WlDataDevice _wlDataDevice;
        private readonly WlDataDeviceHandler _wlDataDeviceHandler;

        private WlDataSourceHandler? _currentDataSourceHandler;

        public WlDataHandler(AvaloniaWaylandPlatform platform)
        {
            _platform = platform;
            _wlDataDevice = platform.WlDataDeviceManager.GetDataDevice(platform.WlSeat);
            _wlDataDeviceHandler = new WlDataDeviceHandler(platform);
            _wlDataDevice.Events = _wlDataDeviceHandler;
        }

        public Task<string> GetTextAsync() => Task.FromResult(_wlDataDeviceHandler.CurrentOffer?.GetText() ?? string.Empty);

        public Task SetTextAsync(string text)
        {
            SetText(text);
            return Task.CompletedTask;
        }

        public Task ClearAsync()
        {
            _wlDataDevice.SetSelection(null, _platform.WlInputDevice.KeyboardEnterSerial);
            return Task.CompletedTask;
        }

        public Task SetDataObjectAsync(IDataObject data)
        {
            foreach (var format in data.GetDataFormats())
            {
                switch (format)
                {
                    case DataFormats.Text:
                        SetText(data.GetText());
                        return Task.CompletedTask;
                    case DataFormats.FileNames:
                        SetUris(data.GetFileNames());
                        return Task.CompletedTask;
                }
            }

            SetDataObject(data);
            return Task.CompletedTask;
        }

        public Task<string[]> GetFormatsAsync() =>
            _wlDataDeviceHandler.CurrentOffer is null
                ? Task.FromResult(Array.Empty<string>())
                : Task.FromResult(_wlDataDeviceHandler.CurrentOffer.GetDataFormats().ToArray());

        public Task<object?> GetDataAsync(string format) =>
            format switch
            {
                DataFormats.Text => Task.FromResult<object?>(_wlDataDeviceHandler.CurrentOffer?.GetText()),
                DataFormats.FileNames => Task.FromResult<object?>(_wlDataDeviceHandler.CurrentOffer?.GetFileNames()),
                _ => Task.FromResult(_wlDataDeviceHandler.CurrentOffer?.Get(format))
            };

        public Task<DragDropEffects> DoDragDrop(PointerEventArgs triggerEvent, IDataObject data, DragDropEffects allowedEffects)
        {
            var window = _platform.WlScreens.ActiveWindow;
            if (window is null)
                return Task.FromResult(DragDropEffects.None);
            var dataSource = _platform.WlDataDeviceManager.CreateDataSource();
            _currentDataSourceHandler = new WlDataSourceHandler(dataSource) { DataObject = data };
            dataSource.Events = _currentDataSourceHandler;
            foreach (var mimeType in MimeTypes.GetMimeTypes(data))
                dataSource.Offer(mimeType);
            _wlDataDevice.StartDrag(dataSource, window.WlSurface, null, _platform.WlInputDevice.Serial);
            return _currentDataSourceHandler.DnD;
        }

        public void Dispose() => _wlDataDevice.Dispose();

        private void SetText(string text)
        {
            var dataSource = _platform.WlDataDeviceManager.CreateDataSource();
            _currentDataSourceHandler = new WlDataSourceHandler(dataSource) { Text = text };
            dataSource.Events = _currentDataSourceHandler;
            dataSource.Offer(MimeTypes.Text);
            dataSource.Offer(MimeTypes.TextUtf8);
            _wlDataDevice.SetSelection(dataSource, _platform.WlInputDevice.KeyboardEnterSerial);
        }

        private void SetUris(IEnumerable<string> uris)
        {
            var dataSource = _platform.WlDataDeviceManager.CreateDataSource();
            _currentDataSourceHandler = new WlDataSourceHandler(dataSource) { Uris = uris };
            dataSource.Events = _currentDataSourceHandler;
            dataSource.Offer(MimeTypes.UriList);
            _wlDataDevice.SetSelection(dataSource, _platform.WlInputDevice.KeyboardEnterSerial);
        }

        private void SetDataObject(IDataObject dataObject)
        {
            var dataSource = _platform.WlDataDeviceManager.CreateDataSource();
            _currentDataSourceHandler = new WlDataSourceHandler(dataSource) { DataObject = dataObject };
            dataSource.Events = _currentDataSourceHandler;
            foreach (var format in MimeTypes.GetMimeTypes(dataObject))
                dataSource.Offer(format);
            _wlDataDevice.SetSelection(dataSource, _platform.WlInputDevice.KeyboardEnterSerial);
        }

        private sealed class WlDataDeviceHandler : IDisposable, WlDataDevice.IEvents
        {
            private readonly AvaloniaWaylandPlatform _platform;

            private Point _position;

            public WlDataDeviceHandler(AvaloniaWaylandPlatform platform)
            {
                _platform = platform;
            }

            public WlDataObject? CurrentOffer { get; private set; }

            public void OnDataOffer(WlDataDevice eventSender, WlDataOffer id)
            {
                CurrentOffer?.Dispose();
                CurrentOffer = new WlDataObject(_platform, id);
            }

            public void OnEnter(WlDataDevice eventSender, uint serial, WlSurface surface, WlFixed x, WlFixed y, WlDataOffer id)
            {
                if (CurrentOffer is null || _platform.WlScreens.ActiveWindow?.InputRoot is null)
                    return;
                _position = new Point((int)x, (int)y);
                var dragDropDevice = AvaloniaLocator.Current.GetRequiredService<IDragDropDevice>();
                var inputRoot = _platform.WlScreens.ActiveWindow.InputRoot;
                var modifiers = _platform.WlInputDevice.RawInputModifiers;
                var args = new RawDragEvent(dragDropDevice, RawDragEventType.DragEnter, inputRoot, _position, CurrentOffer, CurrentOffer.DragDropEffects, modifiers);
                _platform.WlScreens.ActiveWindow.Input?.Invoke(args);
                CurrentOffer.WlDataOffer.SetActions((WlDataDeviceManager.DndActionEnum)args.Effects, (WlDataDeviceManager.DndActionEnum)args.Effects);
            }

            public void OnLeave(WlDataDevice eventSender) => CurrentOffer?.Dispose();

            public void OnMotion(WlDataDevice eventSender, uint time, WlFixed x, WlFixed y)
            {
                var window = _platform.WlScreens.ActiveWindow;
                if (window?.InputRoot is null || CurrentOffer is null)
                    return;
                _position = new Point((int)x, (int)y);
                var dragDropDevice = AvaloniaLocator.Current.GetRequiredService<IDragDropDevice>();
                var modifiers = _platform.WlInputDevice.RawInputModifiers;
                var args = new RawDragEvent(dragDropDevice, RawDragEventType.DragOver, window.InputRoot, _position, CurrentOffer, CurrentOffer.DragDropEffects, modifiers);
                window.Input?.Invoke(args);
                CurrentOffer.WlDataOffer.SetActions((WlDataDeviceManager.DndActionEnum)args.Effects, (WlDataDeviceManager.DndActionEnum)args.Effects);
            }

            public void OnDrop(WlDataDevice eventSender)
            {
                var window = _platform.WlScreens.ActiveWindow;
                if (window?.InputRoot is null || CurrentOffer is null)
                    return;
                var dragDropDevice = AvaloniaLocator.Current.GetRequiredService<IDragDropDevice>();
                var modifiers = _platform.WlInputDevice.RawInputModifiers;
                var args = new RawDragEvent(dragDropDevice, RawDragEventType.Drop, window.InputRoot, _position, CurrentOffer, CurrentOffer.DragDropEffects, modifiers);
                window.Input?.Invoke(args);
            }

            public void OnSelection(WlDataDevice eventSender, WlDataOffer? id)
            {
                if (id is not null)
                    return;
                CurrentOffer?.Dispose();
                CurrentOffer = null;
            }

            public void Dispose() => CurrentOffer?.Dispose();
        }

        public class WlDataSourceHandler : WlDataSource.IEvents
        {
            private readonly WlDataSource _wlDataSource;

            private WlDataDeviceManager.DndActionEnum _dndAction;

            public WlDataSourceHandler(WlDataSource wlDataSource)
            {
                _wlDataSource = wlDataSource;
            }

            public string? Text { get; set; }

            public IEnumerable<string>? Uris { get; set; }

            public IDataObject? DataObject { get; set; }

            private TaskCompletionSource<DragDropEffects>? _dnd;
            public Task<DragDropEffects> DnD => _dnd?.Task ?? (_dnd = new TaskCompletionSource<DragDropEffects>()).Task;

            public void OnTarget(WlDataSource eventSender, string mimeType) { }

            public unsafe void OnSend(WlDataSource eventSender, string mimeType, int fd)
            {
                var content = mimeType switch
                {
                    MimeTypes.Text or MimeTypes.TextUtf8 when Text is not null => Encoding.UTF8.GetBytes(Text),
                    MimeTypes.UriList when Uris is not null => Uris.SelectMany(static x => Encoding.UTF8.GetBytes(x).Append((byte)'\n')).ToArray(),
                    _ when DataObject?.Get(mimeType) is byte[] data => data,
                    _ => null
                };

                if (content is not null)
                    fixed (byte* ptr = content)
                        LibC.write(fd, (IntPtr)ptr, content.Length);

                LibC.close(fd);
            }

            public void OnCancelled(WlDataSource eventSender) => _wlDataSource.Dispose();

            public void OnDndDropPerformed(WlDataSource eventSender)
            {
                if (_dndAction == WlDataDeviceManager.DndActionEnum.Ask)
                    _wlDataSource.Dispose();
            }

            public void OnDndFinished(WlDataSource eventSender)
            {
                var finalAction = (DragDropEffects)_dndAction;
                _dnd!.TrySetResult(finalAction);
            }

            public void OnAction(WlDataSource eventSender, WlDataDeviceManager.DndActionEnum dndAction) => _dndAction = dndAction;
        }
    }
}
