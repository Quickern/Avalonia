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
using NWayland.Protocols.Wayland;

namespace Avalonia.Wayland
{
    internal class WlDataHandler : IClipboard, IPlatformDragSource, IDisposable
    {
        private readonly AvaloniaWaylandPlatform _platform;
        private readonly WlDataDevice _wlDataDevice;
        private readonly WlDataDeviceHandler _wlDataDeviceHandler;

        private WlDataSourceHandler? _currentDataSourceHandler;

        private const string PlainText = "text/plain";
        private const string PlainTextUtf8 = "text/plain;charset=utf-8";
        private const string UriList = "text/uri-list";

        public WlDataHandler(AvaloniaWaylandPlatform platform)
        {
            _platform = platform;
            _wlDataDevice = platform.WlDataDeviceManager.GetDataDevice(platform.WlSeat);
            _wlDataDeviceHandler = new WlDataDeviceHandler(platform);
            _wlDataDevice.Events = _wlDataDeviceHandler;
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
                CurrentOffer = new WlDataObject(id);
            }

            public void OnEnter(WlDataDevice eventSender, uint serial, WlSurface surface, int x, int y, WlDataOffer id)
            {
                if (CurrentOffer is null || _platform.WlScreens.ActiveWindow?.InputRoot is null)
                    return;
                _position = new Point(x, y);
                var dragDropDevice = AvaloniaLocator.Current.GetRequiredService<IDragDropDevice>();
                var inputRoot = _platform.WlScreens.ActiveWindow.InputRoot;
                var modifiers = _platform.WlInputDevice.RawInputModifiers;
                var args = new RawDragEvent(dragDropDevice, RawDragEventType.DragEnter, inputRoot, _position, CurrentOffer, CurrentOffer.DragDropEffects, modifiers);
                _platform.WlScreens.ActiveWindow.Input?.Invoke(args);
                CurrentOffer.WlDataOffer.SetActions((WlDataDeviceManager.DndActionEnum)args.Effects, (WlDataDeviceManager.DndActionEnum)args.Effects);
            }

            public void OnLeave(WlDataDevice eventSender)
            {
                throw new NotImplementedException();
            }

            public void OnMotion(WlDataDevice eventSender, uint time, int x, int y)
            {
                var window = _platform.WlScreens.ActiveWindow;
                if (window?.InputRoot is null || CurrentOffer is null)
                    return;
                _position = new Point(x, y);
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

            public WlDataSourceHandler(WlDataSource wlDataSource)
            {
                _wlDataSource = wlDataSource;
            }

            public string? Text { get; set; }

            public IEnumerable<string>? Uris { get; set; }

            public IDataObject? DataObject { get; set; }

            public void OnTarget(WlDataSource eventSender, string mimeType) { }

            public unsafe void OnSend(WlDataSource eventSender, string mimeType, int fd)
            {
                var content = mimeType switch
                {
                    PlainText or PlainTextUtf8 when Text is not null => Encoding.UTF8.GetBytes(Text),
                    UriList when Uris is not null => Uris.SelectMany(Encoding.UTF8.GetBytes).ToArray(),
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
                throw new NotImplementedException();
            }

            public void OnDndFinished(WlDataSource eventSender)
            {
                throw new NotImplementedException();
            }

            public void OnAction(WlDataSource eventSender, WlDataDeviceManager.DndActionEnum dndAction)
            {
                throw new NotImplementedException();
            }
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
            throw new NotImplementedException();
        }

        public void Dispose() => _wlDataDevice.Dispose();

        private void SetText(string text)
        {
            var dataSource = _platform.WlDataDeviceManager.CreateDataSource();
            _currentDataSourceHandler = new WlDataSourceHandler(dataSource) { Text = text };
            dataSource.Events = _currentDataSourceHandler;
            dataSource.Offer(PlainText);
            dataSource.Offer(PlainTextUtf8);
            _wlDataDevice.SetSelection(dataSource, _platform.WlInputDevice.KeyboardEnterSerial);
        }

        private void SetUris(IEnumerable<string> uris)
        {
            var dataSource = _platform.WlDataDeviceManager.CreateDataSource();
            _currentDataSourceHandler = new WlDataSourceHandler(dataSource) { Uris = uris };
            dataSource.Events = _currentDataSourceHandler;
            dataSource.Offer(UriList);
            _wlDataDevice.SetSelection(dataSource, _platform.WlInputDevice.KeyboardEnterSerial);
        }

        private void SetDataObject(IDataObject dataObject)
        {
            var dataSource = _platform.WlDataDeviceManager.CreateDataSource();
            _currentDataSourceHandler = new WlDataSourceHandler(dataSource) { DataObject = dataObject };
            dataSource.Events = _currentDataSourceHandler;
            foreach (var format in dataObject.GetDataFormats())
                dataSource.Offer(format);
            _wlDataDevice.SetSelection(dataSource, _platform.WlInputDevice.KeyboardEnterSerial);
        }

        private sealed class WlDataObject : IDataObject, IDisposable, WlDataOffer.IEvents
        {
            private readonly List<string> _mimeTypes;

            public WlDataObject(WlDataOffer wlDataOffer)
            {
                WlDataOffer = wlDataOffer;
                WlDataOffer.Events = this;
                _mimeTypes = new List<string>();
            }

            public WlDataOffer WlDataOffer { get; }

            public DragDropEffects DragDropEffects { get; private set; }

            public IEnumerable<string> GetDataFormats() => _mimeTypes;

            public bool Contains(string dataFormat) => _mimeTypes.Contains(dataFormat);

            public unsafe string? GetText()
            {
                var fd = Receive(PlainText);
                if (fd < 0)
                    return null;

                Span<byte> buffer = stackalloc byte[1024];
                var sb = new StringBuilder();
                fixed (byte* ptr = buffer)
                {
                    while (true)
                    {
                        var read = LibC.read(fd, (IntPtr)ptr, 1024);
                        if (read <= 0)
                            break;
                        sb.Append(Encoding.UTF8.GetString(ptr, read));
                    }
                }

                LibC.close(fd);
                return sb.ToString();
            }

            public unsafe IEnumerable<string>? GetFileNames()
            {
                var fd = Receive(UriList);
                if (fd < 0)
                    return null;

                Span<byte> buffer = stackalloc byte[1024];
                var sb = new StringBuilder();
                fixed (byte* ptr = buffer)
                {
                    while (true)
                    {
                        var read = LibC.read(fd, (IntPtr)ptr, 1024);
                        if (read <= 0)
                            break;
                        sb.Append(Encoding.UTF8.GetString(ptr, read));
                    }
                }

                LibC.close(fd);
                return sb.ToString().Split('\n');
            }

            public unsafe object? Get(string dataFormat)
            {
                if (_mimeTypes.Count <= 0)
                    return null;

                var fd = Receive(_mimeTypes[0]);
                if (fd < 0)
                    return null;

                var buffer = new byte[1024];
                var ms = new MemoryStream();
                fixed (byte* ptr = buffer)
                {
                    while (true)
                    {
                        var read = LibC.read(fd, (IntPtr)ptr, 1024);
                        if (read <= 0)
                            break;
                        ms.Write(buffer, 0, read);
                    }
                }

                LibC.close(fd);
                return ms.ToArray();
            }

            public void OnOffer(WlDataOffer eventSender, string mimeType) => _mimeTypes.Add(mimeType);

            public void OnSourceActions(WlDataOffer eventSender, WlDataDeviceManager.DndActionEnum sourceActions) => DragDropEffects |= (DragDropEffects)sourceActions;

            public void OnAction(WlDataOffer eventSender, WlDataDeviceManager.DndActionEnum dndAction) => DragDropEffects = (DragDropEffects)dndAction;

            public void Dispose() => WlDataOffer.Dispose();

            private unsafe int Receive(string mimeType)
            {
                var fds = stackalloc int[2];
                if (LibC.pipe2(fds, FileDescriptorFlags.O_RDONLY) < 0)
                {
                    WlDataOffer.Dispose();
                    return -1;
                }

                WlDataOffer.Receive(mimeType, fds[1]);
                WlDataOffer.Display.Roundtrip();
                LibC.close(fds[1]);
                return fds[0];
            }
        }
    }
}
