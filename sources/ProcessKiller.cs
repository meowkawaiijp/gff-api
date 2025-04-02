using System.Diagnostics;
using System.Runtime.InteropServices;
using System;
using System.Threading;

namespace Goodbye_F__king_File
{
    public class ProcessKiller
    {
        // P/Invoke 宣言: トークン操作用
        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool LookupPrivilegeValue(string lpSystemName, string lpName, out LUID lpLuid);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges, ref TOKEN_PRIVILEGES NewState, uint Zero, IntPtr Null1, IntPtr Null2);

        [StructLayout(LayoutKind.Sequential)]
        public struct LUID
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct TOKEN_PRIVILEGES
        {
            public uint PrivilegeCount;
            public LUID Luid;
            public uint Attributes;
        }

        const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
        const uint TOKEN_QUERY = 0x0008;
        const uint SE_PRIVILEGE_ENABLED = 0x00000002;

        // P/Invoke 宣言: プロセス操作用
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

        const uint PROCESS_TERMINATE = 0x0001;

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

        // SeDebugPrivilege を有効化
        public static bool EnableDebugPrivilege()
        {
            if (!OpenProcessToken(Process.GetCurrentProcess().Handle, TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out IntPtr hToken))
            {
                Logger.Log(Logger.LogType.ERROR, "OpenProcessToken の呼び出しに失敗しました。");
                return false;
            }

            if (!LookupPrivilegeValue(null, "SeDebugPrivilege", out LUID luid))
            {
                Logger.Log(Logger.LogType.ERROR, "LookupPrivilegeValue の呼び出しに失敗しました。");
                return false;
            }

            TOKEN_PRIVILEGES tp;
            tp.PrivilegeCount = 1;
            tp.Luid = luid;
            tp.Attributes = SE_PRIVILEGE_ENABLED;
            if (!AdjustTokenPrivileges(hToken, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero))
            {
                Logger.Log(Logger.LogType.ERROR, "AdjustTokenPrivileges の呼び出しに失敗しました。");
                return false;
            }
            return true;
        }

        // 指定されたプロセスを強制終了
        public static void ForceKillProcess(Process proc)
        {
            // まずはデバッグ特権を有効化
            if (!EnableDebugPrivilege())
            {
                Logger.Log(Logger.LogType.ERROR, "デバッグ特権の有効化に失敗しました。");
                return;
            }

            IntPtr hProcess = OpenProcess(PROCESS_TERMINATE, false, proc.Id);
            if (hProcess == IntPtr.Zero)
            {
                Logger.Log(Logger.LogType.ERROR, $"プロセス {proc.Id} をオープンできませんでした。");
                return;
            }

            if (!TerminateProcess(hProcess, 1))
            {
                Logger.Log(Logger.LogType.ERROR, $"プロセス {proc.Id} の強制終了に失敗しました。");
            }
            else
            {
                Logger.Log(Logger.LogType.INFO, $"プロセス {proc.Id} を強制終了しました。");
                Thread.Sleep(200);
            }
        }
    }
}