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
        private readonly WlDataDevicehandler _wlDataDeviceHandler;
        private readonly IDragDropDevice _dragDropDevice;

        private WlDataSourceHandler? _currentDataSourceHandler;

        private const string PlainText = "text/plain";
        private const string UriList = "text/uri-list";

        public WlDataHandler(AvaloniaWaylandPlatform platform)
        {
            _platform = platform;
            _wlDataDevice = platform.WlDataDeviceManager.GetDataDevice(platform.WlSeat);
            _wlDataDeviceHandler = new WlDataDevicehandler();
            _wlDataDevice.Events = _wlDataDeviceHandler;
            _dragDropDevice = AvaloniaLocator.Current.GetService<IDragDropDevice>()!;
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

            public void OnSelection(WlDataDevice eventSender, WlDataOffer? id)
            {
                if (id is not null) return;
                CurrentOffer?.Dispose();
                CurrentOffer = null;
            }

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
                WlDataOffer.Display.Flush();
                NativeMethods.close(fds[1]);
                return fds[0];
            }

            public void Dispose() => WlDataOffer.Dispose();
        }

        public class WlDataSourceHandler : WlDataSource.IEvents
        {
            public WlDataSource WlDataSource { get; }

            public string? Text { get; set; }

            public IEnumerable<string>? Uris { get; set; }

            public IDataObject? DataObject { get; set; }

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
                var content = mimeType switch
                {
                    PlainText when Text is not null => Encoding.UTF8.GetBytes(Text),
                    UriList when Uris is not null => Uris.SelectMany(Encoding.UTF8.GetBytes).ToArray(),
                    _ when DataObject?.Get(mimeType) is byte[] data => data,
                    _ => null
                };

                if (content is not null)
                    fixed (byte* ptr = content)
                        NativeMethods.write(fd, (IntPtr)ptr, content.Length);

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
            SetText(text);
            return Task.CompletedTask;
        }

        public Task ClearAsync()
        {
            var window = _platform.WlScreens.ActiveWindow;
            if (window is null)
                return Task.CompletedTask;

            _wlDataDevice.SetSelection(null, window.WlInputDevice.KeyboardEnterSerial);
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
                : Task.FromResult(_wlDataDeviceHandler.CurrentOffer.MimeTypes.ToArray());

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

        public void Dispose() => _wlDataDevice.Dispose();

        private unsafe string? GetText()
        {
            var offer = _wlDataDeviceHandler.CurrentOffer;
            if (offer?.MimeTypes.Contains(PlainText) is not true)
                return null;

            var fd = offer.Receive(PlainText);
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
            var offer = _wlDataDeviceHandler.CurrentOffer;

            if (offer?.MimeTypes.Contains(UriList) is not true)
                return null;

            var fd = offer.Receive(UriList);
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
            var offer = _wlDataDeviceHandler.CurrentOffer;
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

        private void SetText(string text)
        {
            var window = _platform.WlScreens.ActiveWindow;
            if (window is null)
                return;

            var dataSource = _platform.WlDataDeviceManager.CreateDataSource();
            _currentDataSourceHandler = new WlDataSourceHandler(dataSource) { Text = text };
            dataSource.Events = _currentDataSourceHandler;
            dataSource.Offer(PlainText);
            _wlDataDevice.SetSelection(dataSource, window.WlInputDevice.KeyboardEnterSerial);
        }

        private void SetUris(IEnumerable<string> uris)
        {
            var window = _platform.WlScreens.ActiveWindow;
            if (window is null)
                return;

            var dataSource = _platform.WlDataDeviceManager.CreateDataSource();
            _currentDataSourceHandler = new WlDataSourceHandler(dataSource) { Uris = uris };
            dataSource.Events = _currentDataSourceHandler;
            dataSource.Offer(UriList);
            _wlDataDevice.SetSelection(dataSource, window.WlInputDevice.KeyboardEnterSerial);
        }

        private void SetDataObject(IDataObject dataObject)
        {
            var window = _platform.WlScreens.ActiveWindow;
            if (window is null)
                return;

            var dataSource = _platform.WlDataDeviceManager.CreateDataSource();
            _currentDataSourceHandler = new WlDataSourceHandler(dataSource) { DataObject = dataObject };
            dataSource.Events = _currentDataSourceHandler;
            foreach (var format in dataObject.GetDataFormats())
                dataSource.Offer(format);
            _wlDataDevice.SetSelection(dataSource, window.WlInputDevice.KeyboardEnterSerial);
        }
    }
}
