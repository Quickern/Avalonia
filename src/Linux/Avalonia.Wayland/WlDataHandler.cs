using System;
using System.Collections.Generic;
using System.IO;
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
        private readonly WlDataDevicehandler _wlDataDevicehandler;
        private readonly IDragDropDevice _dragDropDevice;

        public WlDataHandler(AvaloniaWaylandPlatform platform)
        {
            _platform = platform;
            _wlDataDevice = platform.WlDataDeviceManager.GetDataDevice(platform.WlSeat);
            _wlDataDevicehandler = new WlDataDevicehandler();
            _wlDataDevice.Events = _wlDataDevicehandler;
            _dragDropDevice = AvaloniaLocator.Current.GetService<IDragDropDevice>();
        }

        private class WlDataDevicehandler : IDisposable, WlDataDevice.IEvents
        {
            public WlDataOfferHandler? CurrentOffer { get; private set; }

            public void OnDataOffer(WlDataDevice eventSender, WlDataOffer id)
            {
                CurrentOffer?.Dispose();
                CurrentOffer = new WlDataOfferHandler(id);
            }

            public void OnEnter(WlDataDevice eventSender, uint serial, WlSurface surface, int x, int y, WlDataOffer id)
            {
                throw new NotImplementedException();
            }

            public void OnLeave(WlDataDevice eventSender)
            {
                throw new NotImplementedException();
            }

            public void OnMotion(WlDataDevice eventSender, uint time, int x, int y)
            {
                throw new NotImplementedException();
            }

            public void OnDrop(WlDataDevice eventSender)
            {
                throw new NotImplementedException();
            }

            public void OnSelection(WlDataDevice eventSender, WlDataOffer id) { }

            public void Dispose() => CurrentOffer?.Dispose();
        }

        public class WlDataOfferHandler : IDisposable, WlDataOffer.IEvents
        {
            public WlDataOfferHandler(WlDataOffer wlDataOffer)
            {
                WlDataOffer = wlDataOffer;
                wlDataOffer.Events = this;
            }

            public WlDataOffer WlDataOffer { get; }
            public List<string> MimeTypes { get; } = new();

            public void OnOffer(WlDataOffer eventSender, string mimeType) => MimeTypes.Add(mimeType);

            public void OnSourceActions(WlDataOffer eventSender, WlDataDeviceManager.DndActionEnum sourceActions)
            {
                throw new NotImplementedException();
            }

            public void OnAction(WlDataOffer eventSender, WlDataDeviceManager.DndActionEnum dndAction)
            {
                throw new NotImplementedException();
            }

            public int Receive(string mimeType)
            {
                var fds = new int[2];
                if (NativeMethods.pipe(fds) < 0)
                {
                    WlDataOffer.Dispose();
                    return -1;
                }

                WlDataOffer.Receive(mimeType, fds[1]);
                NativeMethods.close(fds[1]);
                WlDataOffer.Display.Roundtrip();
                return fds[0];
            }

            public void Dispose() => WlDataOffer.Dispose();
        }

        public class WlDataSourceHandler : WlDataSource.IEvents
        {
            public WlDataSource WlDataSource { get; }

            public string? Text { get; set; }

            public List<string>? Uris { get; set; }

            public object? Object { get; set; }

            public WlDataSourceHandler(WlDataSource wlDataSource)
            {
                WlDataSource = wlDataSource;
            }

            public void OnTarget(WlDataSource eventSender, string mimeType)
            {
                throw new NotImplementedException();
            }

            public unsafe void OnSend(WlDataSource eventSender, string mimeType, int fd)
            {
                switch (mimeType)
                {
                    case "text/plain":
                        var bytes = Encoding.UTF8.GetBytes(Text!);
                        fixed (byte* ptr = bytes)
                            NativeMethods.write(fd, (IntPtr)ptr, Text!.Length);
                        break;
                    case "text/uri-list":
                        foreach (var uri in Uris!)
                        {
                            bytes = Encoding.UTF8.GetBytes(uri);
                            fixed (byte* ptr = bytes)
                                NativeMethods.write(fd, (IntPtr)ptr, uri.Length);
                        }
                        break;
                }

                NativeMethods.close(fd);
            }

            public void OnCancelled(WlDataSource eventSender) => WlDataSource.Dispose();

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

        public Task<string?> GetTextAsync() => Task.FromResult(GetText());

        public Task SetTextAsync(string text)
        {
            if (_platform.WlScreens.WlWindows.Count <= 0)
                return Task.CompletedTask;
            var window = _platform.WlScreens.WlWindows.Peek();
            var dataSource = _platform.WlDataDeviceManager.CreateDataSource();
            var dataSourceHandler = new WlDataSourceHandler(dataSource) { Text = text };
            dataSource.Events = dataSourceHandler;
            dataSource.Offer("text/plain");
            _wlDataDevice.SetSelection(dataSource, window.WlInputDevice.KeyboardEnterSerial);
            return Task.CompletedTask;
        }

        public Task ClearAsync()
        {
            if (_platform.WlScreens.WlWindows.Count <= 0)
                return Task.CompletedTask;
            var window = _platform.WlScreens.WlWindows.Peek();
            _wlDataDevice.SetSelection(null, window.WlInputDevice.KeyboardEnterSerial);
            return Task.CompletedTask;
        }

        public Task SetDataObjectAsync(IDataObject data)
        {
            throw new NotImplementedException();
        }

        public Task<string[]?> GetFormatsAsync() =>
            _wlDataDevicehandler.CurrentOffer is null ? Task.FromResult<string[]?>(null) : Task.FromResult(_wlDataDevicehandler.CurrentOffer.MimeTypes.ToArray());

        public Task<object?> GetDataAsync(string format) =>
            format switch
            {
                DataFormats.Text => Task.FromResult<object?>(GetText()),
                DataFormats.FileNames => Task.FromResult<object?>(GetUris()),
                _ => Task.FromResult(GetObject())
            };

        public Task<DragDropEffects> DoDragDrop(PointerEventArgs triggerEvent, IDataObject data, DragDropEffects allowedEffects)
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            _wlDataDevice.Dispose();
        }

        private unsafe string? GetText()
        {
            var offer = _wlDataDevicehandler.CurrentOffer;
            if (offer is null)
                return null;

            var fd = offer.Receive("text/plain");
            if (fd < 0)
                return null;

            Span<byte> buffer = stackalloc byte[1024];
            var sb = new StringBuilder();
            fixed (byte* ptr = buffer)
            {
                while (NativeMethods.read(fd, (IntPtr)ptr, 1024) > 0)
                    sb.Append(Encoding.UTF8.GetString(ptr, 1024));
            }

            NativeMethods.close(fd);
            return sb.ToString();
        }

        private unsafe List<string>? GetUris()
        {
            var offer = _wlDataDevicehandler.CurrentOffer;
            if (offer is null)
                return null;

            var fd = offer.Receive("text/uri-list");
            if (fd < 0)
                return null;

            Span<byte> buffer = stackalloc byte[1024];
            var uris = new List<string>();
            fixed (byte* ptr = buffer)
            {
                while (NativeMethods.read(fd, (IntPtr)ptr, 1024) > 0)
                    uris.Add(Encoding.UTF8.GetString(ptr, 1024));
            }

            NativeMethods.close(fd);
            return uris;
        }

        private unsafe object? GetObject()
        {
            var offer = _wlDataDevicehandler.CurrentOffer;
            if (offer is null || offer.MimeTypes.Count <= 0)
                return null;

            var fd = offer.Receive(offer.MimeTypes[0]);
            if (fd < 0)
                return null;

            var buffer = new byte[1024];
            var ms = new MemoryStream();
            fixed (byte* ptr = buffer)
            {
                while (NativeMethods.read(fd, (IntPtr)ptr, 1024) > 0)
                    ms.Write(buffer, 0, 1024);
            }

            NativeMethods.close(fd);
            return ms.ToArray();
        }
        
        
        
        
        
        
        
        
        
        
        
        
        
        
        /*private readonly AvaloniaWaylandPlatform _platform;
        private readonly WlDataDevice _wlDataDevice;
        private readonly IDragDropDevice _dragDropDevice;

        private WlDataOffer? _lastWlDataOffer;
        private List<string> _mimeTypes;
        private IDataObject? _dataObject;
        private string? _text;

        public WlDataHandler(AvaloniaWaylandPlatform platform)
        {
            _platform = platform;
            _dragDropDevice = AvaloniaLocator.Current.GetService<IDragDropDevice>();
            _wlDataDevice = platform.WlDataDeviceManager.GetDataDevice(platform.WlSeat);
            _wlDataDevice.Events = this;
            _mimeTypes = new List<string>();
        }

        public Task<string?> GetTextAsync() => Task.FromResult(GetText());

        public Task SetTextAsync(string text)
        {
            if (_platform.WlScreens.WlWindows.Count == 0)
                return Task.CompletedTask;
            var window = _platform.WlScreens.WlWindows.Peek();
            var wlDataSource = _platform.WlDataDeviceManager.CreateDataSource();
            wlDataSource.Events = this;
            wlDataSource.Offer("text/plain");
            _wlDataDevice.SetSelection(wlDataSource, window.WlInputDevice.KeyboardEnterSerial);
            return Task.CompletedTask;
        }

        public Task ClearAsync()
        {
            throw new NotImplementedException();
        }

        public Task SetDataObjectAsync(IDataObject data)
        {
            if (_platform.WlScreens.WlWindows.Count == 0)
                return Task.CompletedTask;
            var window = _platform.WlScreens.WlWindows.Peek();
            _mimeTypes.Clear();
            var wlDataSource = _platform.WlDataDeviceManager.CreateDataSource();
            wlDataSource.Events = this;
            foreach (var format in data.GetDataFormats())
            {
                _mimeTypes.Add(format);
                switch (format)
                {
                    case DataFormats.Text:
                        wlDataSource.Offer("text/plain");
                        break;
                    case DataFormats.FileNames:
                        wlDataSource.Offer("text/uri-list");
                        break;
                    default:
                        wlDataSource.Offer(format);
                        break;
                }
            }

            _wlDataDevice.SetSelection(wlDataSource, window.WlInputDevice.KeyboardEnterSerial);
            return Task.CompletedTask;
        }

        public Task<string[]> GetFormatsAsync()
            => Task.FromResult(_mimeTypes.ToArray());

        public Task<object?> GetDataAsync(string format) => format switch
        {
            DataFormats.Text => Task.FromResult<object?>(GetText()),
            DataFormats.FileNames => Task.FromResult<object?>(GetUris()),
            _ => Task.FromResult<object?>(GetByteArray())
        };

        public void OnDataOffer(WlDataDevice eventSender, WlDataOffer id)
        {
            _lastWlDataOffer?.Dispose();
            _mimeTypes.Clear();
            _lastWlDataOffer = id;
            _lastWlDataOffer.Events = this;
        }

        public void OnEnter(WlDataDevice eventSender, uint serial, WlSurface surface, int x, int y, WlDataOffer id)
        {
            
        }

        public void OnLeave(WlDataDevice eventSender)
        {
            
        }

        public void OnMotion(WlDataDevice eventSender, uint time, int x, int y)
        {
            
        }

        public void OnDrop(WlDataDevice eventSender)
        {
            
        }

        public void OnSelection(WlDataDevice eventSender, WlDataOffer id)
        {
            _lastWlDataOffer?.Dispose();
            
        }

        public void OnTarget(WlDataSource eventSender, string mimeType)
        {
            
        }

        public unsafe void OnSend(WlDataSource eventSender, string mimeType, int fd)
        {
            if (!_mimeTypes.Contains(mimeType))
            {
                NativeMethods.close(fd);
                return;
            }

            switch (mimeType)
            {
                case "text/plain":
                    var text = _text ?? _dataObject?.GetText();
                    if (text is null)
                        break;
                    var bytes = Encoding.UTF8.GetBytes(text);
                    fixed (byte* ptr = bytes)
                        NativeMethods.write(fd, (IntPtr)ptr, bytes.Length);
                    break;
                case "text/uri-list":
                    var uris = _dataObject?.GetFileNames();
                    if (uris is null)
                        break;
                    foreach (var uri in uris.Where(static uri => uri is not null))
                    {
                        bytes = Encoding.UTF8.GetBytes(uri);
                        fixed (byte* ptr = bytes)
                            NativeMethods.write(fd, (IntPtr)ptr, bytes.Length);
                    }
                    break;
            }

            NativeMethods.close(fd);
        }

        public void OnCancelled(WlDataSource eventSender) => eventSender.Dispose();

        public void OnDndDropPerformed(WlDataSource eventSender)
        {
            
        }

        public void OnDndFinished(WlDataSource eventSender)
        {
            
        }

        public void OnAction(WlDataSource eventSender, WlDataDeviceManager.DndActionEnum dndAction)
        {
            
        }

        public void OnOffer(WlDataOffer eventSender, string mimeType) => _mimeTypes.Add(mimeType);

        public void OnSourceActions(WlDataOffer eventSender, WlDataDeviceManager.DndActionEnum sourceActions)
        {
            
        }

        public void OnAction(WlDataOffer eventSender, WlDataDeviceManager.DndActionEnum dndAction)
        {
            
        }

        private unsafe string? GetText()
        {
            if (_lastWlDataOffer is null)
                return null;

            var fds = new int[2];
            if (NativeMethods.pipe(fds) < 0)
                _lastWlDataOffer.Dispose();

            _lastWlDataOffer.Receive("text/plain", fds[1]);
            NativeMethods.close(fds[1]);
            _platform.WlDisplay.Roundtrip();

            Span<byte> buffer = stackalloc byte[1024];
            var sb = new StringBuilder();
            fixed (byte* ptr = buffer)
            {
                while (NativeMethods.read(fds[0], (IntPtr)ptr, 1024) > 0)
                    sb.Append(Encoding.UTF8.GetString(ptr, 1024));
            }

            NativeMethods.close(fds[0]);
            return sb.ToString();
        }

        private unsafe List<string>? GetUris()
        {
            if (_lastWlDataOffer is null)
                return null;

            var fds = new int[2];
            if (NativeMethods.pipe(fds) < 0)
                _lastWlDataOffer.Dispose();

            _lastWlDataOffer.Receive("text/uri-list", fds[1]);
            NativeMethods.close(fds[1]);
            _platform.WlDisplay.Roundtrip();

            Span<byte> buffer = stackalloc byte[1024];
            var uris = new List<string>();
            fixed (byte* ptr = buffer)
            {
                while (NativeMethods.read(fds[0], (IntPtr)ptr, 1024) > 0)
                    uris.Add(Encoding.UTF8.GetString(ptr, 1024));
            }

            NativeMethods.close(fds[0]);
            return uris;
        }

        private unsafe byte[]? GetByteArray()
        {
            if (_lastWlDataOffer is null || _mimeTypes.Count <= 0)
                return null;

            var fds = new int[2];
            if (NativeMethods.pipe(fds) < 0)
                _lastWlDataOffer.Dispose();

            _lastWlDataOffer.Receive(_mimeTypes[0], fds[1]);
            NativeMethods.close(fds[1]);
            _platform.WlDisplay.Roundtrip();

            var buffer = new byte[1024];
            var ms = new MemoryStream();
            fixed (byte* ptr = buffer)
            {
                while (NativeMethods.read(fds[0], (IntPtr)ptr, 1024) > 0)
                    ms.Write(buffer, 0, 1024);
            }

            NativeMethods.close(fds[0]);
            return ms.ToArray();
        }*/
    }
}
