using System.Runtime.InteropServices;


namespace Avalonia.FreeDesktop
{
    internal static class FdHelper
    {
        public static int CreateAnonymousFile(int size)
        {
            var fd = NativeMethods.memfd_create("wayland-shm", NativeMethods.MFD_CLOEXEC | NativeMethods.MFD_ALLOW_SEALING);
            if (fd == -1)
                return -1;
            NativeMethods.fcntl(fd, NativeMethods.F_ADD_SEALS, NativeMethods.F_SEAL_SHRINK);
            return ResizeFd(fd, size);
        }

        public static int ResizeFd(int fd, int size)
        {
            int ret;
            do
                ret = NativeMethods.ftruncate(fd, size);
            while (ret < 0 && Marshal.GetLastWin32Error() == NativeMethods.EINTR);
            if (ret >= 0)
                return fd;
            NativeMethods.close(fd);
            return -1;
        }
    }
}
