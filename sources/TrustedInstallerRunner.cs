using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Goodbye_F__king_File
{
    public class TrustedInstallerRunner
    {
        // 定数定義
        const uint TOKEN_ADJUST_PRIVILEGES = 0x20;
        const uint TOKEN_QUERY = 0x8;
        const uint SE_PRIVILEGE_ENABLED = 0x2;
        const uint PROCESS_QUERY_INFORMATION = 0x0400;
        const uint PROCESS_DUP_HANDLE = 0x0040;
        const uint MAXIMUM_ALLOWED = 0x02000000;
        const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
        const uint LOGON_WITH_PROFILE = 0x00000001;
        const uint TH32CS_SNAPPROCESS = 0x00000002;
        const uint GENERIC_READ = 0x80000000;
        const uint GENERIC_EXECUTE = 0x20000000;
        const uint GENERIC_EXECUTE_SC_MANAGER = 0x00020000;
        const int SC_STATUS_PROCESS_INFO = 0;
        const uint CREATE_NO_WINDOW = 0x08000000;
        const uint STARTF_USESTDHANDLES = 0x00000100;
        const uint HANDLE_FLAG_INHERIT = 0x00000001;
        const uint INFINITE = 0xFFFFFFFF;

        #region STRUCT定義

        [StructLayout(LayoutKind.Sequential)]
        public struct LUID
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct LUID_AND_ATTRIBUTES
        {
            public LUID Luid;
            public uint Attributes;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct TOKEN_PRIVILEGES
        {
            public uint PrivilegeCount;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
            public LUID_AND_ATTRIBUTES[] Privileges;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SECURITY_ATTRIBUTES
        {
            public uint nLength;
            public IntPtr lpSecurityDescriptor;
            [MarshalAs(UnmanagedType.Bool)]
            public bool bInheritHandle;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct PROCESSENTRY32
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExeFile;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SERVICE_STATUS_PROCESS
        {
            public uint dwServiceType;
            public uint dwCurrentState;
            public uint dwControlsAccepted;
            public uint dwWin32ExitCode;
            public uint dwServiceSpecificExitCode;
            public uint dwCheckPoint;
            public uint dwWaitHint;
            public uint dwProcessId;
            public uint dwServiceFlags;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct STARTUPINFO
        {
            public uint cb;
            public string lpReserved;
            public string lpDesktop;
            public string lpTitle;
            public uint dwX;
            public uint dwY;
            public uint dwXSize;
            public uint dwYSize;
            public uint dwXCountChars;
            public uint dwYCountChars;
            public uint dwFillAttribute;
            public uint dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public uint dwProcessId;
            public uint dwThreadId;
        }

        #endregion

        #region 諸々のdllをインポート

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool LookupPrivilegeValue(string lpSystemName, string lpName, out LUID lpLuid);

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges,
            ref TOKEN_PRIVILEGES NewState, uint BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool DuplicateTokenEx(IntPtr hExistingToken, uint dwDesiredAccess,
            ref SECURITY_ATTRIBUTES lpTokenAttributes, int ImpersonationLevel, int TokenType, out IntPtr phNewToken);

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool ImpersonateLoggedOnUser(IntPtr hToken);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr OpenSCManager(string lpMachineName, string lpDatabaseName, uint dwDesiredAccess);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr OpenService(IntPtr hSCManager, string lpServiceName, uint dwDesiredAccess);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool QueryServiceStatusEx(IntPtr hService, int InfoLevel, IntPtr lpBuffer, uint cbBufSize, out uint pcbBytesNeeded);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool StartService(IntPtr hService, int dwNumServiceArgs, string[] lpServiceArgVectors);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool CreateProcessWithTokenW(IntPtr hToken, uint dwLogonFlags, string lpApplicationName,
            string lpCommandLine, uint dwCreationFlags, IntPtr lpEnvironment, string lpCurrentDirectory,
            ref STARTUPINFO lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CreatePipe(out IntPtr hReadPipe, out IntPtr hWritePipe, ref SECURITY_ATTRIBUTES lpPipeAttributes, uint nSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetHandleInformation(IntPtr hObject, uint dwMask, uint dwFlags);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        #endregion

        #region 内部処理

        // 必要な特権を有効化する
        static bool EnablePrivilege(string privilegeName)
        {
            if (!OpenProcessToken(System.Diagnostics.Process.GetCurrentProcess().Handle, TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out IntPtr hToken))
            {
                Logger.Log(Logger.LogType.ERROR, $"[EnablePrivilege] OpenProcessTokenの実行に失敗しました (特権: {privilegeName})。エラー: {Marshal.GetLastWin32Error()}");
                return false;
            }
            if (!LookupPrivilegeValue(null, privilegeName, out LUID luid))
            {
                Logger.Log(Logger.LogType.ERROR, $"[EnablePrivilege] LookupPrivilegeValueの実行に失敗しました (特権: {privilegeName})。エラー: {Marshal.GetLastWin32Error()}");
                CloseHandle(hToken);
                return false;
            }
            TOKEN_PRIVILEGES tp = new TOKEN_PRIVILEGES
            {
                PrivilegeCount = 1,
                Privileges = new LUID_AND_ATTRIBUTES[1]
            };
            tp.Privileges[0].Luid = luid;
            tp.Privileges[0].Attributes = SE_PRIVILEGE_ENABLED;

            if (!AdjustTokenPrivileges(hToken, false, ref tp, (uint)Marshal.SizeOf(typeof(TOKEN_PRIVILEGES)), IntPtr.Zero, IntPtr.Zero))
            {
                Logger.Log(Logger.LogType.ERROR, $"[EnablePrivilege] AdjustTokenPrivilegesの実行に失敗しました (特権: {privilegeName})。エラー: {Marshal.GetLastWin32Error()}");
                CloseHandle(hToken);
                return false;
            }
            CloseHandle(hToken);
            Logger.Log(Logger.LogType.DEBUG, $"[EnablePrivilege] 特権 {privilegeName} を正常に有効化しました。");
            return true;
        }

        // 指定プロセス名からプロセスIDを取得（見つからなければ0を返す）
        static uint GetProcessIdByName(string processName)
        {
            IntPtr hSnapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            if (hSnapshot == (IntPtr)(-1))
            {
                Logger.Log(Logger.LogType.ERROR, $"[GetProcessIdByName] CreateToolhelp32Snapshotの実行に失敗しました。エラー: {Marshal.GetLastWin32Error()}");
                return 0;
            }

            PROCESSENTRY32 pe = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32)) };
            uint pid = 0;
            if (Process32First(hSnapshot, ref pe))
            {
                do
                {
                    if (string.Compare(pe.szExeFile, processName, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        pid = pe.th32ProcessID;
                        break;
                    }
                } while (Process32Next(hSnapshot, ref pe));
            }
            else
            {
                Logger.Log(Logger.LogType.ERROR, $"[GetProcessIdByName] Process32Firstの実行に失敗しました。エラー: {Marshal.GetLastWin32Error()}");
                CloseHandle(hSnapshot);
                return 0;
            }
            CloseHandle(hSnapshot);
            if (pid == 0)
                Logger.Log(Logger.LogType.ERROR, $"[GetProcessIdByName] プロセスが見つかりませんでした: {processName}");
            return pid;
        }

        // winlogon.exe のトークンを用いてシステムのインパーソネーションを行う
        static void ImpersonateSystem()
        {
            uint systemPid = GetProcessIdByName("winlogon.exe");
            if (systemPid == 0)
            {
                Logger.Log(Logger.LogType.ERROR, "[ImpersonateSystem] winlogon.exeが見つかりませんでした。");
                return;
            }
            IntPtr hSystemProcess = OpenProcess(PROCESS_DUP_HANDLE | PROCESS_QUERY_INFORMATION, false, systemPid);
            if (hSystemProcess == IntPtr.Zero)
            {
                Logger.Log(Logger.LogType.ERROR, $"[ImpersonateSystem] OpenProcessの実行に失敗しました (winlogon.exe)。エラー: {Marshal.GetLastWin32Error()}");
                return;
            }
            if (!OpenProcessToken(hSystemProcess, MAXIMUM_ALLOWED, out IntPtr hSystemToken))
            {
                Logger.Log(Logger.LogType.ERROR, $"[ImpersonateSystem] OpenProcessTokenの実行に失敗しました (winlogon.exe)。エラー: {Marshal.GetLastWin32Error()}");
                CloseHandle(hSystemProcess);
                return;
            }
            SECURITY_ATTRIBUTES sa = new SECURITY_ATTRIBUTES
            {
                nLength = (uint)Marshal.SizeOf(typeof(SECURITY_ATTRIBUTES)),
                bInheritHandle = false,
                lpSecurityDescriptor = IntPtr.Zero
            };
            if (!DuplicateTokenEx(hSystemToken, MAXIMUM_ALLOWED, ref sa, 2 /* SecurityImpersonation */, 2 /* TokenImpersonation */, out IntPtr hDupToken))
            {
                Logger.Log(Logger.LogType.ERROR, $"[ImpersonateSystem] DuplicateTokenExの実行に失敗しました (winlogon.exe)。エラー: {Marshal.GetLastWin32Error()}");
                CloseHandle(hSystemToken);
                CloseHandle(hSystemProcess);
                return;
            }
            if (!ImpersonateLoggedOnUser(hDupToken))
            {
                Logger.Log(Logger.LogType.ERROR, $"[ImpersonateSystem] ImpersonateLoggedOnUserの実行に失敗しました。エラー: {Marshal.GetLastWin32Error()}");
            }
            else
            {
                Logger.Log(Logger.LogType.INFO, "[ImpersonateSystem] システムのインパーソネーションに成功しました。(SeDebugPrivilege/SeImpersonatePrivilege)");
            }
            CloseHandle(hDupToken);
            CloseHandle(hSystemToken);
            CloseHandle(hSystemProcess);
        }

        // TrustedInstallerサービスを開始し、プロセスIDを返す
        static uint StartTrustedInstallerService()
        {
            Logger.Log(Logger.LogType.DEBUG, "[StartTrustedInstallerService] サービスコントロールマネージャーをオープン中...");
            IntPtr hSCManager = OpenSCManager(null, "ServicesActive", GENERIC_EXECUTE_SC_MANAGER);
            if (hSCManager == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenSCManager failed");

            Logger.Log(Logger.LogType.DEBUG, "[StartTrustedInstallerService] TrustedInstallerサービスをオープン中...");
            IntPtr hService = OpenService(hSCManager, "TrustedInstaller", GENERIC_READ | GENERIC_EXECUTE);
            if (hService == IntPtr.Zero)
            {
                CloseHandle(hSCManager);
                throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenService failed");
            }

            SERVICE_STATUS_PROCESS ssp = new SERVICE_STATUS_PROCESS();
            uint bytesNeeded = 0;
            int sspSize = Marshal.SizeOf(typeof(SERVICE_STATUS_PROCESS));
            IntPtr pStatus = Marshal.AllocHGlobal(sspSize);

            try
            {
                while (QueryServiceStatusEx(hService, SC_STATUS_PROCESS_INFO, pStatus, (uint)sspSize, out bytesNeeded))
                {
                    ssp = Marshal.PtrToStructure<SERVICE_STATUS_PROCESS>(pStatus);
                    Logger.Log(Logger.LogType.DEBUG, $"[StartTrustedInstallerService] サービスの現在の状態: {ssp.dwCurrentState}");
                    // サービスが停止している場合は起動する
                    if (ssp.dwCurrentState == 1) // SERVICE_STOPPED
                    {
                        Logger.Log(Logger.LogType.WARN, "[StartTrustedInstallerService] サービスは停止中です。起動を試みます...");
                        if (!StartService(hService, 0, null))
                        {
                            throw new Win32Exception(Marshal.GetLastWin32Error(), "StartService failed");
                        }
                    }
                    // サービス開始中の場合は待機する
                    if (ssp.dwCurrentState == 2 /* SERVICE_START_PENDING */ ||
                        ssp.dwCurrentState == 3 /* SERVICE_STOP_PENDING */)
                    {
                        Logger.Log(Logger.LogType.DEBUG, $"[StartTrustedInstallerService] サービスの状態は保留中です。{ssp.dwWaitHint} ms待機中...");
                        Thread.Sleep((int)ssp.dwWaitHint);
                        continue;
                    }
                    if (ssp.dwCurrentState == 4) // SERVICE_RUNNING
                    {
                        Logger.Log(Logger.LogType.INFO, $"[StartTrustedInstallerService] サービスは実行中です。PID: {ssp.dwProcessId}");
                        return ssp.dwProcessId;
                    }
                }
            }
            finally
            {
                try
                {
                    Marshal.FreeHGlobal(pStatus);
                    CloseHandle(hService);
                    CloseHandle(hSCManager);
                }
                catch { }
            }
            throw new Win32Exception(Marshal.GetLastWin32Error(), "QueryServiceStatusEx failed");
        }

        // TrustedInstallerプロセスのトークンを用いてコマンドを実行し、
        // 標準出力／エラーのリダイレクト用に匿名パイプを作成し、hReadPipeを返す。
        // 失敗時はPROCESS_INFORMATION.hProcess==IntPtr.Zero
        static PROCESS_INFORMATION CreateProcessAsTrustedInstaller(uint trustedInstallerPid, string commandLine, out IntPtr hReadPipe)
        {
            hReadPipe = IntPtr.Zero;
            PROCESS_INFORMATION pi = new PROCESS_INFORMATION();
            // 必要な特権を有効化
            if (!EnablePrivilege("SeDebugPrivilege") || !EnablePrivilege("SeImpersonatePrivilege"))
            {
                Logger.Log(Logger.LogType.ERROR, "[CreateProcessAsTrustedInstaller] 必要な特権の有効化に失敗しました。");
                return pi;
            }
            // システムにインパーソネート
            ImpersonateSystem();

            IntPtr hTIProcess = OpenProcess(PROCESS_DUP_HANDLE | PROCESS_QUERY_INFORMATION, false, trustedInstallerPid);
            if (hTIProcess == IntPtr.Zero)
            {
                Logger.Log(Logger.LogType.ERROR, $"[CreateProcessAsTrustedInstaller] OpenProcessの実行に失敗しました (TrustedInstaller.exe)。エラー: {Marshal.GetLastWin32Error()}");
                return pi;
            }
            if (!OpenProcessToken(hTIProcess, MAXIMUM_ALLOWED, out IntPtr hTIToken))
            {
                Logger.Log(Logger.LogType.ERROR, $"[CreateProcessAsTrustedInstaller] OpenProcessTokenの実行に失敗しました (TrustedInstaller.exe)。エラー: {Marshal.GetLastWin32Error()}");
                CloseHandle(hTIProcess);
                return pi;
            }
            SECURITY_ATTRIBUTES sa = new SECURITY_ATTRIBUTES
            {
                nLength = (uint)Marshal.SizeOf(typeof(SECURITY_ATTRIBUTES)),
                bInheritHandle = false,
                lpSecurityDescriptor = IntPtr.Zero
            };
            if (!DuplicateTokenEx(hTIToken, MAXIMUM_ALLOWED, ref sa, 2, 2, out IntPtr hDupToken))
            {
                Logger.Log(Logger.LogType.ERROR, $"[CreateProcessAsTrustedInstaller] DuplicateTokenExの実行に失敗しました (TrustedInstaller.exe)。エラー: {Marshal.GetLastWin32Error()}");
                CloseHandle(hTIToken);
                CloseHandle(hTIProcess);
                return pi;
            }
            // 匿名パイプ作成：子プロセスへは書き込みハンドルを継承
            SECURITY_ATTRIBUTES saPipe = new SECURITY_ATTRIBUTES
            {
                nLength = (uint)Marshal.SizeOf(typeof(SECURITY_ATTRIBUTES)),
                bInheritHandle = true,
                lpSecurityDescriptor = IntPtr.Zero
            };
            if (!CreatePipe(out hReadPipe, out IntPtr hWritePipe, ref saPipe, 0))
            {
                Logger.Log(Logger.LogType.ERROR, $"[CreateProcessAsTrustedInstaller] CreatePipeの実行に失敗しました。エラー: {Marshal.GetLastWin32Error()}");
                CloseHandle(hDupToken);
                CloseHandle(hTIToken);
                CloseHandle(hTIProcess);
                return pi;
            }
            // 親側の読み取りハンドルは継承させない
            if (!SetHandleInformation(hReadPipe, HANDLE_FLAG_INHERIT, 0))
            {
                Logger.Log(Logger.LogType.ERROR, $"[CreateProcessAsTrustedInstaller] SetHandleInformationの実行に失敗しました。エラー: {Marshal.GetLastWin32Error()}");
                CloseHandle(hWritePipe);
                CloseHandle(hDupToken);
                CloseHandle(hTIToken);
                CloseHandle(hTIProcess);
                return pi;
            }
            STARTUPINFO si = new STARTUPINFO();
            si.cb = (uint)Marshal.SizeOf(typeof(STARTUPINFO));
            si.lpDesktop = "Winsta0\\Default";
            // 標準出力／エラーをリダイレクト
            si.dwFlags = STARTF_USESTDHANDLES;
            si.hStdOutput = hWritePipe;
            si.hStdError = hWritePipe;
            // CREATE_NO_WINDOWを指定して非表示で実行
            uint creationFlags = CREATE_UNICODE_ENVIRONMENT | CREATE_NO_WINDOW;
            if (!CreateProcessWithTokenW(hDupToken, LOGON_WITH_PROFILE, null, commandLine, creationFlags, IntPtr.Zero, null, ref si, out pi))
            {
                Logger.Log(Logger.LogType.ERROR, $"[CreateProcessAsTrustedInstaller] CreateProcessWithTokenWの実行に失敗しました。エラー: {Marshal.GetLastWin32Error()}");
                CloseHandle(hWritePipe);
                CloseHandle(hDupToken);
                CloseHandle(hTIToken);
                CloseHandle(hTIProcess);
                return pi;
            }
            // 子プロセスにはhWritePipeが継承されるので、親側は閉じる
            CloseHandle(hWritePipe);
            CloseHandle(hDupToken);
            CloseHandle(hTIToken);
            CloseHandle(hTIProcess);
            Logger.Log(Logger.LogType.INFO, "[CreateProcessAsTrustedInstaller] プロセスの作成に成功しました。");
            return pi;
        }

        #endregion

        // TrustedInstallerとしてプロセスを開始し、出力をリダイレクトしてコンソールへ出力、プロセス終了後に自身も終了する
        // commandLineのうちバイナリまでのパスは二重引用符で囲うこと！
        public static int Run(string commandLine)
        {
            try
            {
                Logger.Log(Logger.LogType.INFO, "[Main] TrustedInstallerサービスを開始しています...");
                uint tiPid = StartTrustedInstallerService();
                if (tiPid == 0)
                {
                    Logger.Log(Logger.LogType.ERROR, "[Main] TrustedInstallerサービスの開始に失敗しました。");
                    return 1;
                }
                Logger.Log(Logger.LogType.INFO, "[Main] TrustedInstallerとしてプロセスを作成しています...");

                // プロセス作成と標準出力リダイレクト用パイプの取得
                PROCESS_INFORMATION pi = CreateProcessAsTrustedInstaller(tiPid, commandLine, out IntPtr hReadPipe);
                if (pi.hProcess == IntPtr.Zero)
                {
                    Logger.Log(Logger.LogType.ERROR, "[Main] TrustedInstallerとしてプロセスの作成に失敗しました。");
                    return 1;
                }

                // 別スレッドでリダイレクトされた出力を読み、Logger経由で出力
                Thread outputThread = new Thread(() =>
                {
                    try
                    {
                        using (FileStream fs = new FileStream(new SafeFileHandle(hReadPipe, true), FileAccess.Read))
                        {
                            byte[] buffer = new byte[4096];
                            int bytesRead;
                            while ((bytesRead = fs.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                string output = Encoding.Default.GetString(buffer, 0, bytesRead);
                                Console.Write(output);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log(Logger.LogType.ERROR, $"[OutputThread] リダイレクトされた出力の読み取り中にエラーが発生しました: {ex.Message}");
                    }
                });
                outputThread.IsBackground = true;
                outputThread.Start();

                // 子プロセスの終了まで待機
                WaitForSingleObject(pi.hProcess, INFINITE);
                CloseHandle(pi.hProcess);
                CloseHandle(pi.hThread);
                outputThread.Join();
            }
            catch (Exception ex)
            {
                Logger.Log(Logger.LogType.ERROR, "[Main] 例外: " + ex.Message);
            }
            return 0;
        }
    }
}
