




using System;
using System.Runtime.InteropServices;

namespace UnityInjector.ConsoleUtil;








internal partial class ConsoleEncoding
{
    [DllImport("kernel32.dll")]
    private static extern uint GetConsoleOutputCP();

    [DllImport("kernel32.dll")]
    private static extern uint GetACP();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int MultiByteToWideChar(uint codePage,
                                                  uint dwFlags,
                                                  [In] [MarshalAs(UnmanagedType.LPArray)]
                                                  byte[] lpMultiByteStr,
                                                  int cbMultiByte,
                                                  [Out] [MarshalAs(UnmanagedType.LPWStr)]
                                                  char[] lpWideCharStr,
                                                  int cchWideChar);

    [DllImport("kernel32.dll")]
    private static extern IntPtr SetConsoleOutputCP(uint codepage);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int WideCharToMultiByte(uint codePage,
                                                  uint dwFlags,
                                                  [In] [MarshalAs(UnmanagedType.LPWStr)] char[] lpWideCharStr,
                                                  int cchWideChar,
                                                  [Out] [MarshalAs(UnmanagedType.LPArray)]
                                                  byte[] lpMultiByteStr,
                                                  int cbMultiByte,
                                                  IntPtr lpDefaultChar,
                                                  IntPtr lpUsedDefaultChar);
}
