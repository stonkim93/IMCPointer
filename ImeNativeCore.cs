// ImeNativeCore.cs
#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace IMCPointer
{
    /// <summary>
    /// 대상 창의 현재 입력 상태(IME 모드)를 감지하고 상태를 변경하는 모듈입니다.
    /// </summary>
    internal static class ImeState
    {
        public enum State
        {
            EnglishLower, EnglishUpper, Hangul, PaliUS, JapaneseIME
        }

        private const int MaxCacheSize = 100;
        private static readonly Dictionary<IntPtr, bool> _hangulStateCache = new Dictionary<IntPtr, bool>();

        /// <summary>
        /// 주어진 상태가 한글 입력 기반인지 확인합니다.
        /// </summary>
        public static bool IsHangul(State state) => state == State.Hangul;

        /// <summary>
        /// 현재 포커스된 창의 키보드 레이아웃과 IME 상태를 종합하여 현재 입력 상태를 판별합니다.
        /// </summary>
        public static State Detect(IntPtr foregroundHwnd)
        {
            bool capsOn = (NativeMethods.GetKeyState(NativeMethods.VK_CAPITAL) & 0x0001) != 0;
            if (foregroundHwnd == IntPtr.Zero) return capsOn ? State.EnglishUpper : State.EnglishLower;

            uint threadId = NativeMethods.GetWindowThreadProcessId(foregroundHwnd, out _);
            long hklValue = NativeMethods.GetKeyboardLayout(threadId).ToInt64();
            ushort langId = (ushort)(hklValue & 0xFFFF);

            if (langId == 0x0409) return State.PaliUS;
            if (langId == 0x0411) return State.JapaneseIME;

            if (langId == 0x0412) // 한국어 레이아웃
            {
                bool isHangul = IsHangulModeSystemWide(foregroundHwnd);
                if (isHangul)
                {
                    return State.Hangul;
                }
                return capsOn ? State.EnglishUpper : State.EnglishLower;
            }

            return capsOn ? State.EnglishUpper : State.EnglishLower;
        }

        private static IntPtr GetTargetImeWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return IntPtr.Zero;
            uint threadId = NativeMethods.GetWindowThreadProcessId(hWnd, out _);
            IntPtr focusWnd = hWnd;

            NativeMethods.GUITHREADINFO gti = new() { cbSize = Marshal.SizeOf<NativeMethods.GUITHREADINFO>() };
            if (NativeMethods.GetGUIThreadInfo(threadId, ref gti))
            {
                if (gti.hwndFocus != IntPtr.Zero) focusWnd = gti.hwndFocus;
                else if (gti.hwndActive != IntPtr.Zero) focusWnd = gti.hwndActive;
            }

            IntPtr hIme = NativeMethods.ImmGetDefaultIMEWnd(focusWnd);
            return hIme != IntPtr.Zero ? hIme : NativeMethods.ImmGetDefaultIMEWnd(hWnd);
        }

        /// <summary>
        /// 시스템 전역적으로 현재 창이 한글 입력 모드인지 확인합니다.
        /// </summary>
        public static bool IsHangulModeSystemWide(IntPtr foregroundHwnd)
        {
            return CheckHangulPublic(foregroundHwnd);
        }

        public static bool CheckHangulPublic(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return false;

            if (_hangulStateCache.Count > MaxCacheSize)
            {
                _hangulStateCache.Clear();
            }

            IntPtr hImeWnd = GetTargetImeWindow(hWnd);
            if (hImeWnd != IntPtr.Zero)
            {
                IntPtr res = NativeMethods.SendMessageTimeout(hImeWnd, NativeMethods.WM_IME_CONTROL, (IntPtr)NativeMethods.IMC_GETCONVERSIONMODE, IntPtr.Zero, NativeMethods.SMTO_ABORTIFHUNG, 30, out IntPtr result);
                if (res != IntPtr.Zero)
                {
                    bool isHangul = ((uint)result.ToInt64() & NativeMethods.IME_CMODE_NATIVE) != 0;
                    _hangulStateCache[hWnd] = isHangul;
                    return isHangul;
                }
            }

            IntPtr hIMC = NativeMethods.ImmGetContext(hWnd);
            if (hIMC != IntPtr.Zero)
            {
                bool success = NativeMethods.ImmGetConversionStatus(hIMC, out uint conv, out _);
                NativeMethods.ImmReleaseContext(hWnd, hIMC);
                if (success)
                {
                    bool isHangul = (conv & NativeMethods.IME_CMODE_NATIVE) != 0;
                    _hangulStateCache[hWnd] = isHangul;
                    return isHangul;
                }
            }
            
            return _hangulStateCache.TryGetValue(hWnd, out bool cachedState) ? cachedState : false;
        }

        /// <summary>
        /// 대상 윈도우의 IME 한글/영문 상태를 강제로 설정합니다.
        /// </summary>
        public static void SetHangulState(IntPtr hWnd, bool setHangul)
        {
            IntPtr hImeWnd = GetTargetImeWindow(hWnd);
            if (hImeWnd != IntPtr.Zero)
            {
                NativeMethods.SendMessageTimeout(hImeWnd, NativeMethods.WM_IME_CONTROL, (IntPtr)NativeMethods.IMC_GETCONVERSIONMODE, IntPtr.Zero, NativeMethods.SMTO_ABORTIFHUNG, 20, out IntPtr result);
                uint mode = (uint)result.ToInt64();
                bool isHangul = (mode & NativeMethods.IME_CMODE_NATIVE) != 0;

                if (isHangul != setHangul)
                {
                    if (setHangul) mode |= NativeMethods.IME_CMODE_NATIVE;
                    else mode &= ~NativeMethods.IME_CMODE_NATIVE;
                    NativeMethods.SendMessageTimeout(hImeWnd, NativeMethods.WM_IME_CONTROL, (IntPtr)NativeMethods.IMC_SETCONVERSIONMODE, (IntPtr)mode, NativeMethods.SMTO_ABORTIFHUNG, 20, out _);
                    
                    _hangulStateCache[hWnd] = setHangul;
                }
            }
        }
    }
}