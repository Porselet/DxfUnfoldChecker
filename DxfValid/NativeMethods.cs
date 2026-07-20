using System;
using System.Runtime.InteropServices;

namespace DxfValid
{
    internal static class NativeMethods
    {
        // Импортируем ту самую функцию множественного выделения из ядра Windows
        [DllImport("shell32.dll", ExactSpelling = true)]
        public static extern int SHOpenFolderAndSelectItems(
            IntPtr pidlFolder,
            uint cidl,
            [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl,
            uint dwFlags);

        // Функция для перевода обычного текстового пути папки/файла в системный PIDL
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr ILCreateFromPath([MarshalAs(UnmanagedType.LPWStr)] string pszPath);

        // Функция очистки памяти, чтобы Windows не ругалась на утечки
        [DllImport("shell32.dll", ExactSpelling = true)]
        public static extern void ILFree(IntPtr pidl);
    }
}
