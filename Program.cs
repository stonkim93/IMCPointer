// Program.cs - IMCPointer
#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32; // [수정사항 반영: 시스템 이벤트 감지를 위한 네임스페이스 추가 (빌드 오류 해결)]

[assembly: System.Runtime.CompilerServices.DisableRuntimeMarshalling]

namespace IMCPointer
{
    #region [ 1. 사용자 설정 영역 (AppConfig) ]
    /// <summary>
    /// 애플리케이션의 전역 설정 및 입력 상태별 테마를 정의합니다.
    /// </summary>
    internal static class AppConfig
    {
        // 성능 및 기본 설정
        public const int PollingInterval = 100;
        public static readonly string[] IndicatorTargetApps = { "excel", "hwp" };
        public const float IndicatorSize = 8.0f;
        public const float IndicatorOffset = 20.0f;

        // 트레이 UI 설정
        public const int TrayIconSize = 32;
        public const float TrayLowercaseFontSize = 31F;
        public const float TrayUppercaseFontSize = 32F;

        // 트레이 메뉴 표시 옵션
        public static bool ShowPointerWinDefault = true;           
        public static bool ShowPointerWinColor = true;          
        public static bool ShowPointerNewColor = true;          
        public static bool ShowSmallCircleMenu = true;          

        // 프로그램 시작 시 초기 설정
        public static int DefaultPointerMode = 2;           
        public static bool DefaultEnableMiniIndicator = true;   

        public struct Theme
        {
            public Color PointerColor;   
            public Color TrayBgColor;    
            public Color TrayTextColor;  
            public string TrayText;      
            public string Description;   
            public Color IBeamColor;     
        }

        // 5가지 핵심 IME 입력 상태에 대한 테마 정의
        public static readonly Dictionary<ImeState.State, Theme> Themes = new()
        {
            [ImeState.State.EnglishLower] = new Theme { PointerColor = Color.White, TrayBgColor = Color.Black, TrayTextColor = Color.White, TrayText = "e", Description = "영어 소문자 [e]", IBeamColor = Color.Black },
            [ImeState.State.EnglishUpper] = new Theme { PointerColor = Color.DeepSkyBlue, TrayBgColor = Color.Black, TrayTextColor = Color.DeepSkyBlue, TrayText = "E", Description = "영어 대문자 [E]", IBeamColor = Color.DeepSkyBlue },
            [ImeState.State.Hangul] = new Theme { PointerColor = Color.Red, TrayBgColor = Color.Red, TrayTextColor = Color.White, TrayText = "K", Description = "한글 (Caps Off) [K]", IBeamColor = Color.Red },
            [ImeState.State.PaliUS] = new Theme { PointerColor = Color.Orange, TrayBgColor = Color.Black, TrayTextColor = Color.Orange, TrayText = "p", Description = "Pali어 Unicode [p]", IBeamColor = Color.Orange },
            [ImeState.State.JapaneseIME] = new Theme { PointerColor = Color.Lime, TrayBgColor = Color.Black, TrayTextColor = Color.Lime, TrayText = "j", Description = "Japanese IME [j]", IBeamColor = Color.Lime }
        };
    }
    #endregion

    #region [ 2. 문자열 리소스 (UiText) ]
    /// <summary>
    /// UI에 표시되는 하드코딩된 텍스트 리소스를 관리합니다.
    /// </summary>
    internal static class UiText
    {
        public const string AppName = "IMCPointer";
        public const string AlreadyRunningMessage = "이미 실행 중입니다.";
        public const string FatalErrorPrefix = "치명적 오류:\n";
        public const string StatusChecking = "현재 상태: 확인 중...";
        public const string ExitMenu = "종료(Exit)";
        public const string GithubUrl = "https://github.com/stonkim93/IMCPointer";

        public static string TrayTooltip(string description) => $"{AppName}: {description}";
        public static string StatusLabel(string description) => $"현재 상태: {description}";
    }
    #endregion

    #region [ 3. 그래픽 및 포인터 팩토리 (PointerGraphicsFactory) ]
    /// <summary>
    /// 시스템 커서를 기반으로 색상이 변경된 커스텀 커서 이미지를 생성합니다.
    /// </summary>
    internal static class PointerGraphicsFactory
    {
        public static IntPtr CreateColoredSystemPointer(uint ocrId, Color targetColor, int renderSize)
        {
            IntPtr hPointer = NativeMethods.LoadImage(IntPtr.Zero, (IntPtr)ocrId, NativeMethods.IMAGE_CURSOR, renderSize, renderSize, 0);
            
            if (hPointer == IntPtr.Zero)
                hPointer = NativeMethods.LoadImage(IntPtr.Zero, (IntPtr)ocrId, NativeMethods.IMAGE_CURSOR, 0, 0, NativeMethods.LR_SHARED | NativeMethods.LR_DEFAULTSIZE);

            if (hPointer == IntPtr.Zero) return IntPtr.Zero;

            int hotX = 0, hotY = 0;
            if (NativeMethods.GetIconInfo(hPointer, out NativeMethods.ICONINFO iiPointer))
            {
                hotX = iiPointer.xHotspot; 
                hotY = iiPointer.yHotspot;
                if (iiPointer.hbmColor != IntPtr.Zero) NativeMethods.DeleteObject(iiPointer.hbmColor);
                if (iiPointer.hbmMask != IntPtr.Zero) NativeMethods.DeleteObject(iiPointer.hbmMask);
            }

            using Bitmap? rendered = RenderPointerToArgbBitmap(hPointer, renderSize, out int actualWidth, out int actualHeight);
            if (rendered == null) return IntPtr.Zero;

            RecolorCursorStraight(rendered, targetColor, ocrId);

            Bitmap finalBitmap = rendered;
            Bitmap? outlined = null;

            if (ocrId == NativeMethods.OCR_IBEAM)
            {
                int brightness = (targetColor.R * 299 + targetColor.G * 587 + targetColor.B * 114) / 1000;
                Color outlineColor = brightness > 128 ? Color.Black : Color.White;
                outlined = AddSmoothOutline(rendered, outlineColor);
                finalBitmap = outlined;
            }

            float scaleX = (float)renderSize / actualWidth;
            float scaleY = (float)renderSize / actualHeight;
            int scaledHotX = (int)Math.Round(hotX * scaleX);
            int scaledHotY = (int)Math.Round(hotY * scaleY);

            IntPtr ptr = BitmapToPointer(finalBitmap, scaledHotX, scaledHotY);
            outlined?.Dispose();
            return ptr;
        }

        private static unsafe Bitmap AddSmoothOutline(Bitmap src, Color outlineColor)
        {
            int width = src.Width, height = src.Height;
            Bitmap result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            var srcData = src.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var dstData = result.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

            byte* pSrc = (byte*)srcData.Scan0;
            byte* pDst = (byte*)dstData.Scan0;
            int stride = srcData.Stride;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * stride + x * 4;
                    byte srcA = pSrc[idx + 3];

                    if (srcA == 255)
                    {
                        pDst[idx] = pSrc[idx]; pDst[idx + 1] = pSrc[idx + 1];
                        pDst[idx + 2] = pSrc[idx + 2]; pDst[idx + 3] = 255;
                    }
                    else
                    {
                        int maxNeighborAlpha = 0;
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                if (dx == 0 && dy == 0) continue;
                                int ny = y + dy, nx = x + dx;
                                if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                                {
                                    int nA = pSrc[ny * stride + nx * 4 + 3];
                                    if (nA > maxNeighborAlpha) maxNeighborAlpha = nA;
                                }
                            }
                        }

                        if (srcA > 0)
                        {
                            float alphaRatio = srcA / 255.0f;
                            pDst[idx] = (byte)(pSrc[idx] * alphaRatio + outlineColor.B * (1 - alphaRatio));
                            pDst[idx + 1] = (byte)(pSrc[idx + 1] * alphaRatio + outlineColor.G * (1 - alphaRatio));
                            pDst[idx + 2] = (byte)(pSrc[idx + 2] * alphaRatio + outlineColor.R * (1 - alphaRatio));
                            pDst[idx + 3] = (byte)Math.Max(srcA, maxNeighborAlpha > 0 ? 150 : 0);
                        }
                        else if (maxNeighborAlpha > 0)
                        {
                            pDst[idx] = outlineColor.B; pDst[idx + 1] = outlineColor.G; pDst[idx + 2] = outlineColor.R;
                            pDst[idx + 3] = (byte)(maxNeighborAlpha * 0.6f);
                        }
                        else
                        {
                            pDst[idx] = pDst[idx + 1] = pDst[idx + 2] = pDst[idx + 3] = 0;
                        }
                    }
                }
            }
            src.UnlockBits(srcData);
            result.UnlockBits(dstData);
            return result;
        }

        private static unsafe Bitmap? RenderPointerToArgbBitmap(IntPtr hPointer, int targetSize, out int actualWidth, out int actualHeight)
        {
            actualWidth = targetSize;
            actualHeight = targetSize;
            
            if (NativeMethods.GetIconInfo(hPointer, out NativeMethods.ICONINFO ii))
            {
                IntPtr hBmp = ii.hbmColor != IntPtr.Zero ? ii.hbmColor : ii.hbmMask;
                if (hBmp != IntPtr.Zero)
                {
                    using (Image img = Image.FromHbitmap(hBmp))
                    {
                        actualWidth = img.Width;
                        actualHeight = ii.hbmColor != IntPtr.Zero ? img.Height : img.Height / 2;
                    }
                }
                if (ii.hbmColor != IntPtr.Zero) NativeMethods.DeleteObject(ii.hbmColor);
                if (ii.hbmMask != IntPtr.Zero) NativeMethods.DeleteObject(ii.hbmMask);
            }

            NativeMethods.BITMAPINFO bmi = new() { biSize = sizeof(NativeMethods.BITMAPINFO), biWidth = targetSize, biHeight = -targetSize, biPlanes = 1, biBitCount = 32, biCompression = 0 };
            IntPtr hdcScreen = NativeMethods.GetDC(IntPtr.Zero);
            IntPtr hdcMem = NativeMethods.CreateCompatibleDC(hdcScreen);
            IntPtr hDib = NativeMethods.CreateDIBSection(hdcMem, ref bmi, 0, out IntPtr pBits, IntPtr.Zero, 0);

            if (hDib == IntPtr.Zero) { NativeMethods.DeleteDC(hdcMem); NativeMethods.ReleaseDC(IntPtr.Zero, hdcScreen); return null; }

            IntPtr hOld = NativeMethods.SelectObject(hdcMem, hDib);
            int byteCount = targetSize * targetSize * 4;
            new Span<byte>((void*)pBits, byteCount).Clear();

            const uint DI_NORMAL = 0x0003;
            NativeMethods.DrawIconEx(hdcMem, 0, 0, hPointer, targetSize, targetSize, 0, IntPtr.Zero, DI_NORMAL);

            Bitmap bmp = new Bitmap(targetSize, targetSize, PixelFormat.Format32bppArgb);
            var bmpData = bmp.LockBits(new Rectangle(0, 0, targetSize, targetSize), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            
            byte* src = (byte*)pBits;
            byte* dst = (byte*)bmpData.Scan0;
            
            long alphaSum = 0;
            for (int i = 3; i < byteCount; i += 4) alphaSum += src[i];

            if (alphaSum == 0)
            {
                for (int i = 0; i < byteCount; i += 4)
                {
                    byte b = src[i], g = src[i+1], r = src[i+2];
                    if (r > 0 || g > 0 || b > 0)
                    {
                        dst[i] = b; dst[i+1] = g; dst[i+2] = r; dst[i+3] = 255;
                    }
                    else
                    {
                        dst[i] = dst[i+1] = dst[i+2] = dst[i+3] = 0;
                    }
                }
            }
            else
            {
                for (int i = 0; i < byteCount; i += 4)
                {
                    byte b = src[i], g = src[i+1], r = src[i+2], a = src[i+3];
                    if (a == 0)
                    {
                        dst[i] = dst[i+1] = dst[i+2] = dst[i+3] = 0;
                    }
                    else if (a == 255)
                    {
                        dst[i] = b; dst[i+1] = g; dst[i+2] = r; dst[i+3] = 255;
                    }
                    else
                    {
                        dst[i] = (byte)Math.Min(255, (b * 255) / a);
                        dst[i+1] = (byte)Math.Min(255, (g * 255) / a);
                        dst[i+2] = (byte)Math.Min(255, (r * 255) / a);
                        dst[i+3] = a;
                    }
                }
            }
            
            bmp.UnlockBits(bmpData);

            NativeMethods.SelectObject(hdcMem, hOld);
            NativeMethods.DeleteObject(hDib); NativeMethods.DeleteDC(hdcMem); NativeMethods.ReleaseDC(IntPtr.Zero, hdcScreen);

            return bmp;
        }

        private static unsafe void RecolorCursorStraight(Bitmap bmp, Color targetColor, uint ocrId)
        {
            var bmpData = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            byte* ptr = (byte*)bmpData.Scan0;
            int len = bmp.Width * bmp.Height * 4;

            for (int i = 0; i < len; i += 4)
            {
                byte a = ptr[i + 3];
                if (a == 0) continue;

                byte b = ptr[i], g = ptr[i + 1], r = ptr[i + 2];
                
                if (ocrId == NativeMethods.OCR_NORMAL)
                {
                    float intensity = (r * 0.299f + g * 0.587f + b * 0.114f) / 255.0f;
                    ptr[i] = (byte)(b + (targetColor.B - b) * intensity);
                    ptr[i + 1] = (byte)(g + (targetColor.G - g) * intensity);
                    ptr[i + 2] = (byte)(r + (targetColor.R - r) * intensity);
                }
                else
                {
                    ptr[i] = targetColor.B;
                    ptr[i + 1] = targetColor.G;
                    ptr[i + 2] = targetColor.R;
                }
            }
            bmp.UnlockBits(bmpData);
        }

        private static unsafe IntPtr BitmapToPointer(Bitmap bmp, int hotX, int hotY)
        {
            IntPtr hBmpColor = IntPtr.Zero, hBmpMask = IntPtr.Zero;
            IntPtr hdcScreen = NativeMethods.GetDC(IntPtr.Zero);
            try
            {
                NativeMethods.BITMAPINFO bmi = new() { biSize = sizeof(NativeMethods.BITMAPINFO), biWidth = bmp.Width, biHeight = -bmp.Height, biPlanes = 1, biBitCount = 32, biCompression = 0 };
                hBmpColor = NativeMethods.CreateDIBSection(hdcScreen, ref bmi, 0, out IntPtr pBits, IntPtr.Zero, 0);
                
                if (hBmpColor != IntPtr.Zero)
                {
                    var bmpData = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    byte* pSrc = (byte*)bmpData.Scan0;
                    byte* pDst = (byte*)pBits;
                    int bytes = Math.Abs(bmpData.Stride) * bmp.Height;

                    for (int i = 0; i < bytes; i += 4)
                    {
                        byte a = pSrc[i + 3];
                        if (a == 0)
                        {
                            pDst[i] = pDst[i + 1] = pDst[i + 2] = pDst[i + 3] = 0;
                        }
                        else if (a == 255)
                        {
                            pDst[i] = pSrc[i];
                            pDst[i + 1] = pSrc[i + 1];
                            pDst[i + 2] = pSrc[i + 2];
                            pDst[i + 3] = 255;
                        }
                        else
                        {
                            pDst[i] = (byte)((pSrc[i] * a) / 255);
                            pDst[i + 1] = (byte)((pSrc[i + 1] * a) / 255);
                            pDst[i + 2] = (byte)((pSrc[i + 2] * a) / 255);
                            pDst[i + 3] = a;
                        }
                    }
                    bmp.UnlockBits(bmpData);
                }

                using Bitmap maskBmp = new(bmp.Width, bmp.Height, PixelFormat.Format1bppIndexed);
                hBmpMask = maskBmp.GetHbitmap(); 
                
                NativeMethods.ICONINFO ii = new() { fIcon = 0, xHotspot = hotX, yHotspot = hotY, hbmMask = hBmpMask, hbmColor = hBmpColor };
                return NativeMethods.CreateIconIndirect(ref ii);
            }
            catch { return IntPtr.Zero; }
            finally
            {
                if (hBmpColor != IntPtr.Zero) NativeMethods.DeleteObject(hBmpColor);
                if (hBmpMask != IntPtr.Zero) NativeMethods.DeleteObject(hBmpMask);
                if (hdcScreen != IntPtr.Zero) NativeMethods.ReleaseDC(IntPtr.Zero, hdcScreen);
            }
        }
    }
    #endregion

    #region [ 4. 메인 폼 및 트레이 UI 제어 (MainForm) ]
    /// <summary>
    /// 백그라운드에서 동작하며 IME 상태를 감지하고 트레이 아이콘과 커서, 인디케이터를 업데이트합니다.
    /// </summary>
    internal class MainForm : Form
    {
        // ---------------------------------------------------------
        // 전역 상태 변수
        // ---------------------------------------------------------
        public static MainForm? Instance { get; private set; }
        public static IntPtr LastValidHwnd { get; private set; } = IntPtr.Zero;
        public static IntPtr LastValidFocusHwnd { get; private set; } = IntPtr.Zero;

        // ---------------------------------------------------------
        // 상수 및 UI 설정 캐시
        // ---------------------------------------------------------
        private const int HiddenFormSize = 16;
        private const int HiddenFormLocation = -100;
        private const int HiddenLayeredWindowLocation = -10000;
        private const int WindowPosChangedMessage = 0x001A;
        private const int RebuildRetryAfterWindowPosChangedMs = 800;
        private const int RebuildRetryAfterScaleChangeMs = 1500;
        private const int DisplaySettingsChangedDelayMs = 400;
        private const int UserPreferenceChangedDelayMs = 600;
        private const float PointerDiagonalFactor = 0.7071f;
        private const float IBeamIndicatorYOffsetFactor = 0.65f;
        private const float IndicatorBottomMargin = 4f;

        private static readonly RectangleF TrayIconTextRectLower = new RectangleF(-2.0f, -5.0f, 36f, 36f);
        private static readonly RectangleF TrayIconTextRectUpper = new RectangleF(-2.0f, -3.5f, 36f, 36f);

        // ---------------------------------------------------------
        // 필드 (Field) 선언
        // ---------------------------------------------------------
        private readonly Dictionary<ImeState.State, StateAssets> _assetCache = new();
        private readonly System.Windows.Forms.Timer _stateCheckTimer;
        private readonly NotifyIcon _sysTrayIcon;
        private readonly ContextMenuStrip _trayContextMenu;
        private readonly ToolStripMenuItem _menuItemStatus;

        internal enum PointerMode { WinDefault = 0, WinColor = 1, NewColor = 2 }

        private PointerMode _activePointerMode = (PointerMode)AppConfig.DefaultPointerMode;
        private bool _isMiniIndicatorEnabled = AppConfig.DefaultEnableMiniIndicator;

        private ToolStripMenuItem _menuItemPointerWinDefault = null!;
        private ToolStripMenuItem _menuItemPointerWinColor = null!;
        private ToolStripMenuItem _menuItemPointerNewColor = null!;
        private ToolStripMenuItem _menuItemToggleIndicator = null!;

        private bool _isCurrentProcessTarget = false;
        private bool _lastHangulSyncState = false;

        private ImeState.State _previousImeState = (ImeState.State)(-1);
        private Color _currentIndicatorColor = Color.White;
        private Color _lastRenderedIndicatorColor = Color.Empty;
        private IntPtr _lastForegroundHwnd = IntPtr.Zero;
        private IntPtr _currentContextHwnd = IntPtr.Zero;
        private IntPtr _lastPolledHwnd = IntPtr.Zero; 

        // 그래픽 자원
        private IntPtr _dcIndicatorScreen = IntPtr.Zero;
        private IntPtr _dcIndicatorMem = IntPtr.Zero;
        private IntPtr _hBmpIndicator = IntPtr.Zero;
        private IntPtr _hBmpIndicatorOld = IntPtr.Zero;
        private bool _isIndicatorRendered = false;
        private bool _isPointerInIBeamCell = false;
        private int _lastIndicatorX = int.MinValue;
        private int _lastIndicatorY = int.MinValue;

        private float _currentDpiScale = 1.0f;
        private float _physIndicatorOffsetX = 0f;
        private int _indicatorCanvasSize = 16;
        private int _pointerPhysicalSize = 32;

        private IntPtr _lastAppliedArrowHandle = IntPtr.Zero;
        private static readonly unsafe int s_bmiSize = sizeof(NativeMethods.BITMAPINFO);
        private static readonly uint s_currentProcessId = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;

        private class StateAssets : IDisposable
        {
            public IntPtr ArrowNewPtr = IntPtr.Zero;
            public IntPtr IBeamNewPtr = IntPtr.Zero;
            public IntPtr ArrowWinPtr = IntPtr.Zero;
            public IntPtr IBeamWinPtr = IntPtr.Zero;
            public IntPtr IBeamCompareHandleNew = IntPtr.Zero;
            public IntPtr IBeamCompareHandleWin = IntPtr.Zero;
            public Icon? TrayIcon;
            public Color DotColor;
            public string Description = "";

            public void Dispose()
            {
                if (ArrowNewPtr != IntPtr.Zero) NativeMethods.DestroyCursor(ArrowNewPtr);
                if (IBeamNewPtr != IntPtr.Zero) NativeMethods.DestroyCursor(IBeamNewPtr);
                if (ArrowWinPtr != IntPtr.Zero) NativeMethods.DestroyCursor(ArrowWinPtr);
                if (IBeamWinPtr != IntPtr.Zero) NativeMethods.DestroyCursor(IBeamWinPtr);
                if (IBeamCompareHandleNew != IntPtr.Zero) NativeMethods.DestroyCursor(IBeamCompareHandleNew);
                if (IBeamCompareHandleWin != IntPtr.Zero) NativeMethods.DestroyCursor(IBeamCompareHandleWin);
                TrayIcon?.Dispose();
            }
        }

        // ---------------------------------------------------------
        // 폼 생성자 및 초기화
        // ---------------------------------------------------------
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000080 | 0x00000020 | 0x00080000 | 0x08000000 | 0x00000008; // Layered, ToolWindow, Topmost 등
                return cp;
            }
        }

        public MainForm()
        {
            Instance = this;
            this.Size = new Size(HiddenFormSize, HiddenFormSize);
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.Manual;
            this.Location = new Point(HiddenFormLocation, HiddenFormLocation);

            _trayContextMenu = new ContextMenuStrip();
            _menuItemStatus = new ToolStripMenuItem(UiText.StatusChecking) { Enabled = false };

            BuildTrayMenu();

            _sysTrayIcon = new NotifyIcon { Text = UiText.AppName, ContextMenuStrip = _trayContextMenu, Visible = true };
            _sysTrayIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left) { NativeMethods.SetForegroundWindow(this.Handle); _trayContextMenu.Show(Cursor.Position); }
            };

            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

            RebuildStateAssets();

            _stateCheckTimer = new System.Windows.Forms.Timer { Interval = AppConfig.PollingInterval };
            _stateCheckTimer.Tick += ProcessStateCheck;
        }

        // ---------------------------------------------------------
        // 트레이 메뉴 구성 (불필요 메뉴 제거 완료)
        // ---------------------------------------------------------
        private void BuildTrayMenu()
        {
            var titleMenuItem = new ToolStripMenuItem(UiText.AppName, null, (s, e) =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = UiText.GithubUrl, UseShellExecute = true }); }
                catch (Exception ex) { MessageBox.Show($"웹페이지를 열 수 없습니다.\n{ex.Message}", UiText.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error); }
            });
            titleMenuItem.Font = new Font(titleMenuItem.Font, FontStyle.Bold); 
            _trayContextMenu.Items.Add(titleMenuItem);
            _trayContextMenu.Items.Add(_menuItemStatus);
            _trayContextMenu.Items.Add(new ToolStripSeparator());

            _menuItemPointerWinDefault = AddMenuToggle("WIN Default Pointer", AppConfig.ShowPointerWinDefault, (s, e) => UpdatePointerMode(PointerMode.WinDefault));
            _menuItemPointerWinColor = AddMenuToggle("WIN Color Pointer", AppConfig.ShowPointerWinColor, (s, e) => UpdatePointerMode(PointerMode.WinColor));
            _menuItemPointerNewColor = AddMenuToggle("NEW Color Pointer", AppConfig.ShowPointerNewColor, (s, e) => UpdatePointerMode(PointerMode.NewColor));
            AddMenuSeparatorIf(AppConfig.ShowPointerWinDefault || AppConfig.ShowPointerWinColor || AppConfig.ShowPointerNewColor);

            _menuItemToggleIndicator = AddMenuToggle("한글/엑셀 작은원 표시", AppConfig.ShowSmallCircleMenu, (s, e) =>
            {
                _isMiniIndicatorEnabled = _menuItemToggleIndicator.Checked;
                if (!_isMiniIndicatorEnabled) UpdateLayeredIndicator(Color.Transparent, HiddenLayeredWindowLocation, HiddenLayeredWindowLocation);
            });
            _menuItemToggleIndicator.CheckOnClick = true;
            _menuItemToggleIndicator.Checked = _isMiniIndicatorEnabled;

            AddMenuSeparatorIf(AppConfig.ShowSmallCircleMenu);
            _trayContextMenu.Items.Add(new ToolStripMenuItem(UiText.ExitMenu, null, (s, e) => this.Close()));

            SyncPointerMenuChecks();
        }

        private ToolStripMenuItem AddMenuToggle(string text, bool show, EventHandler onClick)
        {
            var item = new ToolStripMenuItem(text, null, onClick);
            if (show) _trayContextMenu.Items.Add(item);
            return item;
        }

        private void AddMenuSeparatorIf(bool condition)
        {
            if (condition) _trayContextMenu.Items.Add(new ToolStripSeparator());
        }

        // ---------------------------------------------------------
        // 이벤트 핸들러 및 폼 오버라이드
        // ---------------------------------------------------------
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WindowPosChangedMessage) Task.Delay(200).ContinueWith(_ => this.BeginInvoke(new Action(() => RebuildAssetsWithRetry(RebuildRetryAfterWindowPosChangedMs))));
            base.WndProc(ref m);
        }

        protected override void OnPaint(PaintEventArgs e) { }
        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            _currentContextHwnd = NativeMethods.GetForegroundWindow();
            _lastPolledHwnd = _currentContextHwnd; 
            _lastForegroundHwnd = _currentContextHwnd;
            _isCurrentProcessTarget = EvaluateTargetProcess(_currentContextHwnd);
            _lastHangulSyncState = ImeState.IsHangulModeSystemWide(_currentContextHwnd);
            
            if (_currentContextHwnd != IntPtr.Zero && !IsTaskbarWindow(_currentContextHwnd) && !IsAppOrTrayWindow(_currentContextHwnd))
            {
                LastValidHwnd = _currentContextHwnd;
                LastValidFocusHwnd = SearchFocusedInputHwnd(_currentContextHwnd);
            }

            ApplyVisualState(ImeState.Detect(_currentContextHwnd));
            _stateCheckTimer.Start();
        }

        private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        {
            if (this.InvokeRequired) { this.BeginInvoke(new Action(() => OnDisplaySettingsChanged(sender, e))); return; }
            Task.Delay(DisplaySettingsChangedDelayMs).ContinueWith(_ => this.BeginInvoke(new Action(() => RebuildAssetsWithRetry(RebuildRetryAfterScaleChangeMs))));
        }

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.Accessibility || e.Category == UserPreferenceCategory.Mouse)
            {
                if (this.InvokeRequired) { this.BeginInvoke(new Action(() => OnUserPreferenceChanged(sender, e))); return; }
                Task.Delay(UserPreferenceChangedDelayMs).ContinueWith(_ => this.BeginInvoke(new Action(() => RebuildAssetsWithRetry(RebuildRetryAfterScaleChangeMs))));
            }
        }

        private void UpdatePointerMode(PointerMode mode)
        {
            _activePointerMode = mode;
            SyncPointerMenuChecks();
            _previousImeState = (ImeState.State)(-1);
            if (mode == PointerMode.WinColor)
            {
                _stateCheckTimer.Stop(); RebuildStateAssets(); _stateCheckTimer.Start();
            }
        }

        private void SyncPointerMenuChecks()
        {
            if (_menuItemPointerWinDefault != null) _menuItemPointerWinDefault.Checked = (_activePointerMode == PointerMode.WinDefault);
            if (_menuItemPointerWinColor != null) _menuItemPointerWinColor.Checked = (_activePointerMode == PointerMode.WinColor);
            if (_menuItemPointerNewColor != null) _menuItemPointerNewColor.Checked = (_activePointerMode == PointerMode.NewColor);
        }

        // ---------------------------------------------------------
        // 상태 확인 및 동기화 로직 (타이머)
        // ---------------------------------------------------------
        private void ProcessStateCheck(object? sender, EventArgs e)
        {
            IntPtr actualHFore = NativeMethods.GetForegroundWindow();
            bool isFocusChanged = (actualHFore != _lastPolledHwnd);
            
            bool isTaskbar = IsTaskbarWindow(actualHFore);
            bool isTrayOrApp = IsAppOrTrayWindow(actualHFore);

            CacheLastValidWindows(actualHFore, isTaskbar, isTrayOrApp);
            SyncSystemHangulState(actualHFore, isTaskbar, isTrayOrApp, isFocusChanged);

            _lastPolledHwnd = actualHFore;

            IntPtr contextHwnd = ResolveContextHwnd(actualHFore);
            TrackCurrentWindow(contextHwnd, isTaskbar, isTrayOrApp);

            ImeState.State currentState = ImeState.Detect(contextHwnd);

            if (currentState != _previousImeState)
            {
                _previousImeState = currentState;
                ApplyVisualState(currentState);
            }

            RenderMiniIndicator(currentState);
        }

        private void CacheLastValidWindows(IntPtr actualHFore, bool isTaskbar, bool isTrayOrApp)
        {
            if (!isTaskbar && !isTrayOrApp && actualHFore != IntPtr.Zero && actualHFore != this.Handle)
            {
                LastValidHwnd = actualHFore;
                LastValidFocusHwnd = SearchFocusedInputHwnd(actualHFore);
            }
        }

        private void SyncSystemHangulState(IntPtr actualHFore, bool isTaskbar, bool isTrayOrApp, bool isFocusChanged)
        {
            bool isCurrentHangul = ImeState.IsHangulModeSystemWide(actualHFore);

            if (isFocusChanged)
            {
                if (LastValidHwnd != IntPtr.Zero)
                {
                    bool isValidHangul = ImeState.IsHangulModeSystemWide(LastValidHwnd);
                    if ((isTaskbar || isTrayOrApp) && isValidHangul != isCurrentHangul)
                    {
                        ImeState.SetHangulState(actualHFore, isValidHangul);
                        isCurrentHangul = ImeState.IsHangulModeSystemWide(actualHFore);
                    }
                }
                _lastHangulSyncState = isCurrentHangul;
            }
            else if (isCurrentHangul != _lastHangulSyncState)
            {
                _lastHangulSyncState = isCurrentHangul;

                Action<IntPtr> SetState = (hwnd) => { if (hwnd != IntPtr.Zero && hwnd != actualHFore) ImeState.SetHangulState(hwnd, isCurrentHangul); };
                
                SetState(LastValidHwnd);
                SetState(this.Handle);
            }
        }

        private IntPtr ResolveContextHwnd(IntPtr actualHFore) => (LastValidHwnd != IntPtr.Zero) ? LastValidHwnd : actualHFore;

        private void TrackCurrentWindow(IntPtr contextHwnd, bool isTaskbar, bool isTrayOrApp)
        {
            if (contextHwnd != _currentContextHwnd)
            {
                if (!isTaskbar && !isTrayOrApp)
                {
                    _lastForegroundHwnd = contextHwnd;
                    _isCurrentProcessTarget = EvaluateTargetProcess(contextHwnd);
                    _isPointerInIBeamCell = false;
                }
                _currentContextHwnd = contextHwnd;
            }
        }

        // ---------------------------------------------------------
        // 에셋 및 UI 렌더링 
        // ---------------------------------------------------------
        private void RebuildAssetsWithRetry(int retryDelayMs)
        {
            _stateCheckTimer.Stop(); RebuildStateAssets(); _stateCheckTimer.Start();
            int currentPhysSize = _pointerPhysicalSize;
            if (retryDelayMs > 0)
            {
                Task.Delay(retryDelayMs).ContinueWith(_ => this.BeginInvoke(new Action(() =>
                {
                    int sysCursorWidth = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXCURSOR);
                    int expectedPhys = sysCursorWidth > 0 ? sysCursorWidth : Math.Max(32, (int)Math.Round(32 * _currentDpiScale));
                    if (expectedPhys != currentPhysSize) { _stateCheckTimer.Stop(); RebuildStateAssets(); _stateCheckTimer.Start(); }
                })));
            }
        }

        private void RebuildStateAssets()
        {
            bool trayWasVisible = false;
            try { trayWasVisible = _sysTrayIcon?.Visible ?? false; } catch { }

            foreach (var asset in _assetCache.Values) try { asset.Dispose(); } catch { }
            _assetCache.Clear(); RestoreDefaults();

            float dpi = 96f;
            IntPtr hFore = NativeMethods.GetForegroundWindow();
            if (hFore != IntPtr.Zero)
            {
                IntPtr hMonitor = NativeMethods.MonitorFromWindow(hFore, NativeMethods.MONITOR_DEFAULTTONEAREST);
                if (hMonitor != IntPtr.Zero && NativeMethods.GetDpiForMonitor(hMonitor, NativeMethods.MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 && dpiX > 0) dpi = dpiX;
            }
            else { uint sysDpi = NativeMethods.GetDpiForSystem(); if (sysDpi > 0) dpi = sysDpi; }

            _currentDpiScale = dpi / 96f;
            int sysCursorWidth = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXCURSOR);
            _pointerPhysicalSize = sysCursorWidth > 0 ? sysCursorWidth : Math.Max(32, (int)Math.Round(32 * _currentDpiScale));
            _physIndicatorOffsetX = _pointerPhysicalSize * 0.5f;
            
            bool winColorFailed = false;

            foreach (ImeState.State state in Enum.GetValues(typeof(ImeState.State)))
            {
                if (!AppConfig.Themes.TryGetValue(state, out AppConfig.Theme t)) continue;
                try
                {
                    IntPtr hArrowNew = PointerGraphicsFactory.CreateColoredSystemPointer(NativeMethods.OCR_NORMAL, t.PointerColor, _pointerPhysicalSize);
                    IntPtr hIBeamNew = PointerGraphicsFactory.CreateColoredSystemPointer(NativeMethods.OCR_IBEAM, t.IBeamColor, _pointerPhysicalSize);
                    IntPtr hArrowWin = PointerGraphicsFactory.CreateColoredSystemPointer(NativeMethods.OCR_NORMAL, t.PointerColor, _pointerPhysicalSize);
                    IntPtr hIBeamWin = PointerGraphicsFactory.CreateColoredSystemPointer(NativeMethods.OCR_IBEAM, t.IBeamColor == Color.White ? Color.Black : t.IBeamColor, _pointerPhysicalSize);

                    if (hArrowWin == IntPtr.Zero) { hArrowWin = NativeMethods.CopyIcon(hArrowNew); winColorFailed = true; }
                    if (hIBeamWin == IntPtr.Zero) { hIBeamWin = NativeMethods.CopyIcon(hIBeamNew); winColorFailed = true; }

                    _assetCache[state] = new StateAssets
                    {
                        DotColor = t.PointerColor, Description = t.Description, ArrowNewPtr = hArrowNew, IBeamNewPtr = hIBeamNew,
                        ArrowWinPtr = hArrowWin, IBeamWinPtr = hIBeamWin,
                        TrayIcon = BuildTrayIcon(t.TrayText, t.TrayBgColor, t.TrayTextColor),
                        IBeamCompareHandleNew = NativeMethods.CopyIcon(hIBeamNew), IBeamCompareHandleWin = NativeMethods.CopyIcon(hIBeamWin)
                    };
                }
                catch { }
            }

            try
            {
                if (trayWasVisible && _sysTrayIcon != null)
                {
                    _sysTrayIcon.Visible = true;
                    ImeState.State st = _previousImeState == (ImeState.State)(-1) ? ImeState.State.EnglishLower : _previousImeState;
                    if (_assetCache.TryGetValue(st, out var ast) && ast.TrayIcon != null) _sysTrayIcon.Icon = ast.TrayIcon;
                }
            } catch { }

            if (_activePointerMode == PointerMode.WinColor && winColorFailed)
            {
                _activePointerMode = PointerMode.NewColor; SyncPointerMenuChecks(); _previousImeState = (ImeState.State)(-1);
            }
        }

        private static Icon BuildTrayIcon(string text, Color bg, Color fg)
        {
            using Bitmap bmp = new(AppConfig.TrayIconSize, AppConfig.TrayIconSize);
            using Graphics g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using SolidBrush bgBrush = new(bg); g.FillRectangle(bgBrush, 0, 0, AppConfig.TrayIconSize, AppConfig.TrayIconSize);
            
            bool lower = !string.IsNullOrEmpty(text) && char.IsLower(text[0]);
            using Font font = new(lower ? "Segoe Print" : "Segoe UI Black", lower ? AppConfig.TrayLowercaseFontSize : AppConfig.TrayUppercaseFontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            using SolidBrush fgBrush = new(fg);
            using StringFormat sf = new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };

            RectangleF rect = lower ? TrayIconTextRectLower : TrayIconTextRectUpper;
            if (lower)
            {
                g.DrawString(text, font, fgBrush, new RectangleF(rect.X, rect.Y, rect.Width, rect.Height), sf);
                g.DrawString(text, font, fgBrush, new RectangleF(rect.X + 1f, rect.Y, rect.Width, rect.Height), sf);
                g.DrawString(text, font, fgBrush, new RectangleF(rect.X, rect.Y + 1f, rect.Width, rect.Height), sf);
                g.DrawString(text, font, fgBrush, new RectangleF(rect.X + 1f, rect.Y + 1f, rect.Width, rect.Height), sf);
                g.DrawString(text, font, fgBrush, new RectangleF(rect.X + 0.5f, rect.Y + 0.5f, rect.Width, rect.Height), sf);
            }
            else g.DrawString(text, font, fgBrush, rect, sf);

            IntPtr hIcon = bmp.GetHicon(); Icon icon = (Icon)Icon.FromHandle(hIcon).Clone(); NativeMethods.DestroyIcon(hIcon); return icon;
        }

        private void ApplyVisualState(ImeState.State state)
        {
            if (!_assetCache.TryGetValue(state, out StateAssets? assets)) return;
            _currentIndicatorColor = assets.DotColor;

            try { if (assets.TrayIcon != null && (_sysTrayIcon.Icon == null || _sysTrayIcon.Icon.Handle != assets.TrayIcon.Handle)) _sysTrayIcon.Icon = assets.TrayIcon; }
            catch { _sysTrayIcon.Icon = assets.TrayIcon; }

            switch (_activePointerMode)
            {
                case PointerMode.WinDefault: RestoreDefaults(); _lastAppliedArrowHandle = IntPtr.Zero; break;
                case PointerMode.WinColor:
                case PointerMode.NewColor:
                    IntPtr hArr = NativeMethods.CopyIcon(_activePointerMode == PointerMode.WinColor ? assets.ArrowWinPtr : assets.ArrowNewPtr);
                    IntPtr hIb = NativeMethods.CopyIcon(_activePointerMode == PointerMode.WinColor ? assets.IBeamWinPtr : assets.IBeamNewPtr);
                    _lastAppliedArrowHandle = hArr;
                    if (hArr != IntPtr.Zero) { if (!NativeMethods.SetSystemCursor(hArr, NativeMethods.OCR_NORMAL)) NativeMethods.DestroyCursor(hArr); }
                    if (hIb != IntPtr.Zero) { if (!NativeMethods.SetSystemCursor(hIb, NativeMethods.OCR_IBEAM)) NativeMethods.DestroyCursor(hIb); }
                    break;
            }

            _sysTrayIcon.Text = UiText.TrayTooltip(assets.Description);
            _menuItemStatus.Text = UiText.StatusLabel(assets.Description);
        }

        // ---------------------------------------------------------
        // 인디케이터 유틸리티
        // ---------------------------------------------------------
        private void RenderMiniIndicator(ImeState.State state)
        {
            if (!NativeMethods.GetCursorPos(out NativeMethods.POINT pt)) return;
            if (_isCurrentProcessTarget && _isMiniIndicatorEnabled)
            {
                bool isIBeam = EvaluatePointerIsIBeam(state);
                if (isIBeam != _isPointerInIBeamCell) { UpdateLayeredIndicator(Color.Transparent, HiddenLayeredWindowLocation, HiddenLayeredWindowLocation); _isPointerInIBeamCell = isIBeam; }
                if (!_isPointerInIBeamCell)
                {
                    float tx = pt.X + (EvaluatePointerIsArrow() ? PointerDiagonalFactor * AppConfig.IndicatorOffset * (_pointerPhysicalSize / 32f) : _physIndicatorOffsetX);
                    float ty = pt.Y + (EvaluatePointerIsArrow() ? PointerDiagonalFactor * AppConfig.IndicatorOffset * (_pointerPhysicalSize / 32f) : _pointerPhysicalSize * IBeamIndicatorYOffsetFactor);
                    if (ty < pt.Y + _pointerPhysicalSize + IndicatorBottomMargin) ty = pt.Y + _pointerPhysicalSize + IndicatorBottomMargin;
                    UpdateLayeredIndicator(_currentIndicatorColor, (int)Math.Round(tx - _indicatorCanvasSize / 2f), (int)Math.Round(ty - _indicatorCanvasSize / 2f));
                }
                else UpdateLayeredIndicator(Color.Transparent, HiddenLayeredWindowLocation, HiddenLayeredWindowLocation);
            }
            else UpdateLayeredIndicator(Color.Transparent, HiddenLayeredWindowLocation, HiddenLayeredWindowLocation);
        }

        private bool EvaluatePointerIsIBeam(ImeState.State state)
        {
            NativeMethods.CURSORINFO ci = new() { cbSize = Marshal.SizeOf<NativeMethods.CURSORINFO>() };
            if (!NativeMethods.GetCursorInfo(ref ci) || ci.hCursor == IntPtr.Zero || !_assetCache.TryGetValue(state, out var a)) return false;
            return ci.hCursor == (_activePointerMode == PointerMode.WinColor ? a.IBeamCompareHandleWin : a.IBeamCompareHandleNew);
        }

        private bool EvaluatePointerIsArrow()
        {
            if (_activePointerMode == PointerMode.WinDefault)
            {
                try
                {
                    var ci = new NativeMethods.CURSORINFO { cbSize = Marshal.SizeOf<NativeMethods.CURSORINFO>() };
                    if (NativeMethods.GetCursorInfo(ref ci) && NativeMethods.GetIconInfo(ci.hCursor, out var ii))
                    {
                        bool isArr = ii.xHotspot == 0 && ii.yHotspot == 0;
                        if (ii.hbmMask != IntPtr.Zero) NativeMethods.DeleteObject(ii.hbmMask);
                        if (ii.hbmColor != IntPtr.Zero) NativeMethods.DeleteObject(ii.hbmColor);
                        return isArr;
                    }
                } catch { } return false;
            }
            if (_previousImeState == (ImeState.State)(-1) || !_assetCache.TryGetValue(_previousImeState, out var a)) return false;
            var cInfo = new NativeMethods.CURSORINFO { cbSize = Marshal.SizeOf<NativeMethods.CURSORINFO>() };
            return NativeMethods.GetCursorInfo(ref cInfo) && cInfo.hCursor != IntPtr.Zero && cInfo.hCursor != (_activePointerMode == PointerMode.WinColor ? a.IBeamCompareHandleWin : a.IBeamCompareHandleNew);
        }

        private static IntPtr SearchFocusedInputHwnd(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return IntPtr.Zero;
            NativeMethods.GUITHREADINFO gti = new() { cbSize = Marshal.SizeOf<NativeMethods.GUITHREADINFO>() };
            if (NativeMethods.GetGUIThreadInfo(NativeMethods.GetWindowThreadProcessId(hWnd, out _), ref gti))
            {
                if (gti.hwndFocus != IntPtr.Zero) return gti.hwndFocus;
                if (gti.hwndActive != IntPtr.Zero) return gti.hwndActive;
            }
            return hWnd;
        }

        private unsafe bool IsTaskbarWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return false;
            Span<char> nm = stackalloc char[256];
            fixed (char* p = nm)
            {
                int len = NativeMethods.GetClassName(hWnd, p, 256);
                if (len > 0) { var s = nm.Slice(0, len); return s.IndexOf("Shell_TrayWnd") >= 0 || s.IndexOf("NotifyIconOverflowWindow") >= 0; }
                return false;
            }
        }

        private unsafe bool IsAppOrTrayWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero || hWnd == this.Handle) return true;
            NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid); if (pid == s_currentProcessId) return true;
            Span<char> nm = stackalloc char[256];
            fixed (char* p = nm)
            {
                int len = NativeMethods.GetClassName(hWnd, p, 256);
                if (len > 0) { var s = nm.Slice(0, len); return s.IndexOf("Progman") >= 0 || s.IndexOf("WorkerW") >= 0 || s.IndexOf("#32768") >= 0; }
                return false;
            }
        }

        private static bool EvaluateTargetProcess(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return false;
            NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid); if (pid == 0) return false;
            try { string n = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; foreach (string a in AppConfig.IndicatorTargetApps) if (n.Equals(a, StringComparison.OrdinalIgnoreCase)) return true; } catch { } return false;
        }

        public static void RestoreDefaults() => NativeMethods.SystemParametersInfo(NativeMethods.SPI_SETCURSORS, 0, IntPtr.Zero, NativeMethods.SPIF_SENDCHANGE);

        private void UpdateLayeredIndicator(Color c, int x, int y)
        {
            bool update = false;
            if (c != _lastRenderedIndicatorColor) { _lastRenderedIndicatorColor = c; if (c != Color.Transparent) RenderIndicatorBuffer(c); update = true; }
            if (x != _lastIndicatorX || y != _lastIndicatorY) { _lastIndicatorX = x; _lastIndicatorY = y; update = true; }
            if (!update) return;

            NativeMethods.SIZE sz = new() { cx = _indicatorCanvasSize, cy = _indicatorCanvasSize };
            NativeMethods.POINT src = new() { X = 0, Y = 0 }, dst = new() { X = x, Y = y };
            NativeMethods.BLENDFUNCTION bf = new() { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };

            if (c == Color.Transparent || !_isIndicatorRendered)
            {
                if (_dcIndicatorMem != IntPtr.Zero)
                {
                    dst.X = -10000; dst.Y = -10000; bf.SourceConstantAlpha = 0;
                    IntPtr sDc = NativeMethods.GetDC(IntPtr.Zero);
                    _ = NativeMethods.UpdateLayeredWindow(this.Handle, sDc, ref dst, ref sz, _dcIndicatorMem, ref src, 0, ref bf, 2);
                    _ = NativeMethods.ReleaseDC(IntPtr.Zero, sDc);
                }
                return;
            }
            IntPtr curDc = NativeMethods.GetDC(IntPtr.Zero);
            _ = NativeMethods.UpdateLayeredWindow(this.Handle, curDc, ref dst, ref sz, _dcIndicatorMem, ref src, 0, ref bf, 2);
            _ = NativeMethods.ReleaseDC(IntPtr.Zero, curDc);
        }

        private void RenderIndicatorBuffer(Color c)
        {
            if (_dcIndicatorMem != IntPtr.Zero) { if (_hBmpIndicatorOld != IntPtr.Zero) NativeMethods.SelectObject(_dcIndicatorMem, _hBmpIndicatorOld); NativeMethods.DeleteDC(_dcIndicatorMem); _dcIndicatorMem = IntPtr.Zero; }
            if (_hBmpIndicator != IntPtr.Zero) { NativeMethods.DeleteObject(_hBmpIndicator); _hBmpIndicator = IntPtr.Zero; }
            if (_dcIndicatorScreen != IntPtr.Zero) { NativeMethods.ReleaseDC(IntPtr.Zero, _dcIndicatorScreen); _dcIndicatorScreen = IntPtr.Zero; }
            if (c == Color.Transparent) { _isIndicatorRendered = false; return; }

            float sz = AppConfig.IndicatorSize * _currentDpiScale, pW = 1.0f;
            _indicatorCanvasSize = (int)Math.Ceiling(sz + (pW * 2) + 6); if (_indicatorCanvasSize % 2 != 0) _indicatorCanvasSize++;

            using Bitmap bmp = new(_indicatorCanvasSize, _indicatorCanvasSize, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.Clear(Color.Transparent);
                float ct = _indicatorCanvasSize / 2f, r = sz / 2f;
                using SolidBrush b = new(c); g.FillEllipse(b, ct - r, ct - r, sz, sz);
                using Pen p = new(c == Color.White ? Color.Black : (c == Color.Black ? Color.White : Color.Black), pW); g.DrawEllipse(p, ct - r, ct - r, sz, sz);
            }
            _dcIndicatorScreen = NativeMethods.GetDC(IntPtr.Zero); _dcIndicatorMem = NativeMethods.CreateCompatibleDC(_dcIndicatorScreen);
            
            NativeMethods.BITMAPINFO bmi = new() { biSize = s_bmiSize, biWidth = bmp.Width, biHeight = -bmp.Height, biPlanes = 1, biBitCount = 32, biCompression = 0 };
            _hBmpIndicator = NativeMethods.CreateDIBSection(_dcIndicatorScreen, ref bmi, 0, out IntPtr pBits, IntPtr.Zero, 0);
            if (_hBmpIndicator != IntPtr.Zero)
            {
                var dat = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
                int b = Math.Abs(dat.Stride) * bmp.Height; unsafe { Buffer.MemoryCopy((void*)dat.Scan0, (void*)pBits, b, b); } bmp.UnlockBits(dat);
            }
            _hBmpIndicatorOld = NativeMethods.SelectObject(_dcIndicatorMem, _hBmpIndicator); _isIndicatorRendered = true;
        }
    }
    #endregion

    #region [ 5. 진입점 (Main) ]
    /// <summary>
    /// 애플리케이션의 진입점입니다. 중복 실행을 방지하고 예외 처리를 수행합니다.
    /// </summary>
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            using Mutex mutex = new Mutex(true, "IMCPointer_SingleInstance", out bool first);
            if (!first)
            {
                MessageBox.Show(UiText.AlreadyRunningMessage, UiText.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            AppDomain.CurrentDomain.UnhandledException += (s, e) => MainForm.RestoreDefaults();
            AppDomain.CurrentDomain.ProcessExit += (s, e) => MainForm.RestoreDefaults();
            Application.ThreadException += (s, e) => MainForm.RestoreDefaults();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            try
            {
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                MessageBox.Show($"{UiText.FatalErrorPrefix}{ex.Message}", UiText.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
    #endregion
}