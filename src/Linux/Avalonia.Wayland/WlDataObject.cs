using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Avalonia.FreeDesktop;
using Avalonia.Input;
using NWayland.Protocols.Wayland;

namespace Avalonia.Wayland
{
    internal sealed class WlDataObject : IDataObject, IDisposable, WlDataOffer.IEvents
    {
        private readonly AvaloniaWaylandPlatform _platform;
        private readonly List<string> _mimeTypes;

        public WlDataObject(AvaloniaWaylandPlatform platform, WlDataOffer wlDataOffer)
        {
            _platform = platform;
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
            var fd = Receive(MimeTypes.Text);
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
            var fd = Receive(MimeTypes.UriList);
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
            _platform.WlDisplay.Roundtrip();
            LibC.close(fds[1]);
            return fds[0];
        }
    }
}
