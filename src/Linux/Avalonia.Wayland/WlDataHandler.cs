using System;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Input.Raw;
using NWayland.Protocols.Wayland;

namespace Avalonia.Wayland
{
    public class WlDataHandler : IClipboard, WlDataDevice.IEvents, WlDataSource.IEvents
    {
        private readonly AvaloniaWaylandPlatform _platform;
        private readonly WlDataDevice _wlDataDevice;
        private readonly IDragDropDevice _dragDropDevice;

        private WlDataOffer _lastWlDataOffer;

        public WlDataHandler(AvaloniaWaylandPlatform platform)
        {
            _platform = platform;
            _dragDropDevice = AvaloniaLocator.Current.GetService<IDragDropDevice>();
            //_wlDataDevice = platform.WlDataDeviceManager.GetDataDevice(platform.WlSeat);
            //_wlDataDevice.Events = this;
        }

        public Task<string> GetTextAsync()
        {
            throw new NotImplementedException();
        }

        public Task SetTextAsync(string text)
        {
            throw new NotImplementedException();
        }

        public Task ClearAsync()
        {
            throw new NotImplementedException();
        }

        public Task SetDataObjectAsync(IDataObject data)
        {
            var wlDataSource = _platform.WlDataDeviceManager.CreateDataSource();
            wlDataSource.Events = this;
            foreach (var format in data.GetDataFormats())
                wlDataSource.Offer(format);
            return Task.CompletedTask;
        }

        public Task<string[]> GetFormatsAsync()
        {
            throw new NotImplementedException();
        }

        public Task<object> GetDataAsync(string format)
        {
            throw new NotImplementedException();
        }

        public void OnDataOffer(WlDataDevice eventSender, WlDataOffer id)
        {
            _lastWlDataOffer = id;
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

        public void OnSelection(WlDataDevice eventSender, WlDataOffer id)
        {
            throw new NotImplementedException();
        }

        public void OnTarget(WlDataSource eventSender, string mimeType)
        {
            throw new NotImplementedException();
        }

        public void OnSend(WlDataSource eventSender, string mimeType, int fd)
        {
            throw new NotImplementedException();
        }

        public void OnCancelled(WlDataSource eventSender)
        {
            throw new NotImplementedException();
        }

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
}
