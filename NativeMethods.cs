// NativeMethods.cs
#nullable enable
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace IMCPointer
{
    internal static unsafe partial class NativeMethods
    {
        #region Constants
        public const int VK_CAPITAL = 0x14;                 // Caps Lock 키의 가상 키 코드
        public const int WM_IME_CONTROL = 0x0283;           // IME 제어 메시지
        public const int IMC_GETCONVERSIONMODE = 0x0001;    // IME 변환 모드 가져오기
        public const int IMC_SETCONVERSIONMODE = 0x0002;    // IME 변환 모드 설정
        public const uint IME_CMODE_NATIVE = 0x0001;        // IME 변환 모드: 한글 모드
        public const uint SMTO_ABORTIFHUNG = 0x0002;        // SendMessageTimeout 플래그: 응답이 없으면 중단
        public const uint OCR_NORMAL = 32512;               // 일반 커서
        public const uint OCR_IBEAM = 32513;                // I-beam 커서
        public const uint SPI_SETCURSORS = 0x0057;          // 시스템 커서 설정
        public const uint SPIF_SENDCHANGE = 0x0002;         // 시스템 파라미터 변경 시 모든 창에 알림
        public const int MDT_EFFECTIVE_DPI = 0;             // 모니터 DPI 가져오기: 실제 DPI
        public const uint MONITOR_DEFAULTTONEAREST = 0x00000002; // 기본 모니터: 가장 가까운 모니터
        public const uint IMAGE_CURSOR = 2;                 // LoadImage에서 커서 이미지를 로드할 때 사용
        public const uint LR_SHARED = 0x00008000;           // LoadImage에서 공유 리소스를 로드할 때 사용
        public const uint LR_DEFAULTSIZE = 0x00000040;      // LoadImage에서 기본 크기로 이미지를 로드할 때 사용
        public const int SM_CXCURSOR = 13;                  // 커서의 너비
        public const int SM_CYCURSOR = 14;                  // 커서의 높이
        #endregion

        #region Structs
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx, cy; }
        [StructLayout(LayoutKind.Sequential)] public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
        [StructLayout(LayoutKind.Sequential)] public struct ICONINFO { public int fIcon, xHotspot, yHotspot; public IntPtr hbmMask, hbmColor; }
        [StructLayout(LayoutKind.Sequential)] public struct GUITHREADINFO { public int cbSize, flags; public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret; public int rectLeft, rectTop, rectRight, rectBottom; }
        [StructLayout(LayoutKind.Sequential)] public struct BITMAPINFO { public int biSize, biWidth, biHeight; public short biPlanes, biBitCount; public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant; }
        [StructLayout(LayoutKind.Sequential)] public struct CURSORINFO { public int cbSize, flags; public IntPtr hCursor; public POINT ptScreenPos; }
        #endregion

        #region User32 (General)
        // [수정: 미사용 ClientToScreen 함수 제거 완료]
        [LibraryImport("user32.dll", EntryPoint = "LoadImageW", SetLastError = true)] public static partial IntPtr LoadImage(IntPtr hinst, IntPtr name, uint type, int cx, int cy, uint fuLoad);
        [LibraryImport("user32.dll", EntryPoint = "GetDpiForSystem")] public static partial uint GetDpiForSystem();
        [LibraryImport("user32.dll", EntryPoint = "MonitorFromWindow")] public static partial IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
        [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool GetCursorInfo(ref CURSORINFO pci);
        [LibraryImport("user32.dll", EntryPoint = "GetIconInfo")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);
        [LibraryImport("user32.dll")][SuppressGCTransition] public static partial IntPtr GetForegroundWindow();
        [LibraryImport("user32.dll")][SuppressGCTransition] public static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        [LibraryImport("user32.dll")][SuppressGCTransition] public static partial IntPtr GetKeyboardLayout(uint idThread);
        [LibraryImport("user32.dll")][SuppressGCTransition] public static partial short GetKeyState(int keyCode);
        [LibraryImport("user32.dll")][SuppressGCTransition][return: MarshalAs(UnmanagedType.Bool)] public static partial bool GetCursorPos(out POINT lpPoint);
        [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW")] public static partial IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);
        [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool SetSystemCursor(IntPtr hcur, uint id);
        [LibraryImport("user32.dll")] public static partial IntPtr CopyIcon(IntPtr hIcon);
        [LibraryImport("user32.dll")] public static partial IntPtr CreateIconIndirect(ref ICONINFO iconinfo);
        [LibraryImport("user32.dll", EntryPoint = "GetClassNameW", StringMarshalling = StringMarshalling.Utf16)] public static partial int GetClassName(IntPtr hWnd, char* lpClassName, int nMaxCount);
        [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool SetForegroundWindow(IntPtr hWnd);
        [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);
        [LibraryImport("user32.dll")] public static partial int GetSystemMetrics(int nIndex);
        [LibraryImport("user32.dll", EntryPoint = "GetDC")] public static partial IntPtr GetDC(IntPtr hWnd);
        [LibraryImport("user32.dll", EntryPoint = "ReleaseDC")] public static partial int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [LibraryImport("user32.dll", EntryPoint = "DrawIconEx")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool DrawIconEx(IntPtr hdc, int xLeft, int yTop, IntPtr hIcon, int cxWidth, int cyWidth, uint istepIfAniCur, IntPtr hbrFlickerFreeDraw, uint diFlags);
        [LibraryImport("user32.dll", EntryPoint = "DestroyCursor")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool DestroyCursor(IntPtr hCursor);
        [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool DestroyIcon(IntPtr hIcon);
        [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);
        [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);
        #endregion

        #region Gdi32
        [LibraryImport("gdi32.dll", EntryPoint = "CreateCompatibleDC")] public static partial IntPtr CreateCompatibleDC(IntPtr hdc);
        [LibraryImport("gdi32.dll", EntryPoint = "DeleteDC")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool DeleteDC(IntPtr hdc);
        [LibraryImport("gdi32.dll", EntryPoint = "CreateDIBSection")] public static partial IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint iUsage, out IntPtr ppvBits, IntPtr hSection, uint dwOffset);
        [LibraryImport("gdi32.dll", EntryPoint = "SelectObject")] public static partial IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);
        [LibraryImport("gdi32.dll", EntryPoint = "DeleteObject")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool DeleteObject(IntPtr hObject);
        #endregion

        #region Imm32
        [LibraryImport("imm32.dll")][SuppressGCTransition] public static partial IntPtr ImmGetDefaultIMEWnd(IntPtr hWnd);
        [LibraryImport("imm32.dll")][SuppressGCTransition] public static partial IntPtr ImmGetContext(IntPtr hWnd);
        [LibraryImport("imm32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool ImmGetConversionStatus(IntPtr hIMC, out uint lpfdwConversion, out uint lpfdwSentence);
        [LibraryImport("imm32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);
        #endregion

        #region Shcore
        [LibraryImport("shcore.dll")] public static partial int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
        // [수정: 미사용 GetModuleHandle, GlobalLock, GlobalUnlock 함수 제거 완료]
        #endregion
    }
}