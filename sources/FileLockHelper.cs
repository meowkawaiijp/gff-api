using Goodbye_F__king_File;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System;

namespace Goodbye_F__king_File
{
    public static class FileLockHelper
    {
        // 定数などの定義
        private const int SystemHandleInformation = 16;
        private const uint STATUS_INFO_LENGTH_MISMATCH = 0xC0000004;
        private const uint DUPLICATE_CLOSE_SOURCE = 0x00000001;
        private const int ObjectNameInformation = 1;
        private const int PROCESS_DUP_HANDLE = 0x0040;

        // NTAPI: NtQuerySystemInformation
        [DllImport("ntdll.dll")]
        private static extern uint NtQuerySystemInformation(
            int SystemInformationClass,
            IntPtr SystemInformation,
            uint SystemInformationLength,
            ref uint ReturnLength);

        // NTAPI: NtQueryObject
        [DllImport("ntdll.dll")]
        private static extern uint NtQueryObject(
            IntPtr ObjectHandle,
            int ObjectInformationClass,
            IntPtr ObjectInformation,
            uint ObjectInformationLength,
            ref uint ReturnLength);

        // Win32 API: DuplicateHandle
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DuplicateHandle(
            IntPtr hSourceProcessHandle,
            ushort hSourceHandle,
            IntPtr hTargetProcessHandle,
            out IntPtr lpTargetHandle,
            uint dwDesiredAccess,
            bool bInheritHandle,
            uint dwOptions);

        // Win32 API: OpenProcess
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        // Win32 API: CloseHandle
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        // システムハンドル情報の構造体（各ハンドルの情報）
        [StructLayout(LayoutKind.Sequential)]
        struct SYSTEM_HANDLE_ENTRY
        {
            public int OwnerPid;
            public byte ObjectType;
            public byte HandleFlags;
            public ushort HandleValue;
            public IntPtr ObjectPointer;
            public uint AccessMask;
        }

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        static extern int RmRegisterResources(uint pSessionHandle,
            uint nFiles,
            string[] rgsFilenames,
            uint nApplications,
            [In] RM_UNIQUE_PROCESS[] rgApplications,
            uint nServices,
            string[] rgsServiceNames);

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        static extern int RmGetList(uint dwSessionHandle,
            out uint pnProcInfoNeeded,
            ref uint pnProcInfo,
            [In, Out] RM_PROCESS_INFO[] rgAffectedApps,
            ref uint lpdwRebootReasons);

        // Restart Manager API 関連の構造体・定数定義
        [StructLayout(LayoutKind.Sequential)]
        struct RM_UNIQUE_PROCESS
        {
            public int dwProcessId;
            [Obsolete]
            public FILETIME ProcessStartTime;
        }

        enum RM_APP_TYPE
        {
            RmUnknownApp = 0,
            RmMainWindow = 1,
            RmOtherWindow = 2,
            RmService = 3,
            RmExplorer = 4,
            RmConsole = 5
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct RM_PROCESS_INFO
        {
            public RM_UNIQUE_PROCESS Process;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string strAppName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string strServiceShortName;
            public RM_APP_TYPE ApplicationType;
            public uint AppStatus;
            public uint TSSessionId;
            [MarshalAs(UnmanagedType.Bool)]
            public bool bRestartable;
        }
        [DllImport("rstrtmgr.dll")]
        static extern int RmEndSession(uint pSessionHandle);

        // 指定されたファイルをロックしているプロセスの一覧を取得
        public static List<Process> GetLockingProcesses(string path)
        {
            uint handle;
            string sessionKey = Guid.NewGuid().ToString();

            int res = RmStartSession(out handle, 0, sessionKey);
            if (res != 0)
                throw new Exception("Restart Manager セッションの開始に失敗しました。");

            try
            {
                string[] resources = new string[] { path };
                res = RmRegisterResources(handle, (uint)resources.Length, resources, 0, null, 0, null);
                if (res != 0)
                    throw new Exception("リソースの登録に失敗しました。");

                uint pnProcInfoNeeded = 0;
                uint pnProcInfo = 0;
                uint lpdwRebootReasons = 0;

                // 必要なプロセス情報のサイズを問い合わせる
                res = RmGetList(handle, out pnProcInfoNeeded, ref pnProcInfo, null, ref lpdwRebootReasons);
                if (res != 0 && res != 234) // ERROR_MORE_DATA
                    throw new Exception("プロセス情報の取得に失敗しました。");

                RM_PROCESS_INFO[] processInfo = new RM_PROCESS_INFO[pnProcInfoNeeded];
                pnProcInfo = pnProcInfoNeeded;

                res = RmGetList(handle, out pnProcInfoNeeded, ref pnProcInfo, processInfo, ref lpdwRebootReasons);
                if (res != 0)
                    throw new Exception("プロセス情報の取得に失敗しました。");

                var lockingProcesses = new List<Process>();
                foreach (var procInfo in processInfo)
                {
                    try
                    {
                        var proc = Process.GetProcessById(procInfo.Process.dwProcessId);
                        lockingProcesses.Add(proc);
                    }
                    catch
                    {
                        // プロセスが既に終了している場合はスキップ
                    }
                }
                return lockingProcesses;
            }
            finally
            {
                RmEndSession(handle);
            }
        }


        // 指定したプロセスの中で、対象ファイルに関連するハンドルを強制的に閉じる
        public static bool ForceCloseFileHandle(Process proc, string filePath)
        {
            bool anyClosed = false;
            IntPtr procHandle = OpenProcess(PROCESS_DUP_HANDLE, false, proc.Id);
            if (procHandle == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "プロセスハンドルの取得に失敗しました。");

            try
            {
                // システムハンドル情報の取得
                uint handleInfoSize = 0x10000;
                IntPtr handleInfoPtr = Marshal.AllocHGlobal((int)handleInfoSize);
                try
                {
                    uint retLength = 0;
                    uint ntStatus = NtQuerySystemInformation(SystemHandleInformation, handleInfoPtr, handleInfoSize, ref retLength);
                    while (ntStatus == STATUS_INFO_LENGTH_MISMATCH)
                    {
                        Marshal.FreeHGlobal(handleInfoPtr);
                        handleInfoSize = retLength;
                        handleInfoPtr = Marshal.AllocHGlobal((int)handleInfoSize);
                        ntStatus = NtQuerySystemInformation(SystemHandleInformation, handleInfoPtr, handleInfoSize, ref retLength);
                    }
                    if (ntStatus != 0)
                        throw new Exception("NtQuerySystemInformation に失敗しました。NTSTATUS: 0x" + ntStatus.ToString("X"));

                    // 先頭の Int32 はハンドルの数を示す
                    int handleCount = Marshal.ReadInt32(handleInfoPtr);
                    IntPtr handleEntryPtr = IntPtr.Add(handleInfoPtr, sizeof(int));

                    int sizeOfEntry = Marshal.SizeOf(typeof(SYSTEM_HANDLE_ENTRY));

                    // 対象プロセスのハンドルを走査
                    for (int i = 0; i < handleCount; i++)
                    {
                        SYSTEM_HANDLE_ENTRY entry = Marshal.PtrToStructure<SYSTEM_HANDLE_ENTRY>(handleEntryPtr);
                        if (entry.OwnerPid != proc.Id)
                        {
                            handleEntryPtr = IntPtr.Add(handleEntryPtr, sizeOfEntry);
                            continue;
                        }

                        // DuplicateHandle を用いて対象ハンドルを自プロセスへ複製（読み取り専用）
                        if (DuplicateHandle(procHandle, entry.HandleValue, Process.GetCurrentProcess().Handle, out IntPtr dupHandle, 0, false, 0))
                        {
                            try
                            {
                                // ハンドルからオブジェクト名を取得
                                string objectName = GetObjectName(dupHandle);
                                if (!string.IsNullOrEmpty(objectName) && objectName.IndexOf(filePath, StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    // 一致した場合、DUPLICATE_CLOSE_SOURCE 指定でハンドルを複製し、元側を閉じる
                                    IntPtr dummy;
                                    bool dupClose = DuplicateHandle(procHandle, entry.HandleValue, Process.GetCurrentProcess().Handle, out dummy, 0, false, DUPLICATE_CLOSE_SOURCE);
                                    if (dupClose)
                                    {
                                        anyClosed = true;
                                        Logger.Log(Logger.LogType.INFO, $"プロセス {proc.Id} のハンドル 0x{entry.HandleValue:X} を閉じました。（対象: {objectName}）");
                                    }
                                    else
                                    {
                                        Logger.Log(Logger.LogType.ERROR, $"プロセス {proc.Id} のハンドル 0x{entry.HandleValue:X} のクローズに失敗しました: {Marshal.GetLastWin32Error()}");
                                    }
                                }
                            }
                            finally
                            {
                                CloseHandle(dupHandle);
                            }
                        }
                        handleEntryPtr = IntPtr.Add(handleEntryPtr, sizeOfEntry);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(handleInfoPtr);
                }
            }
            finally
            {
                CloseHandle(procHandle);
            }
            return anyClosed;
        }

        // NtQueryObject を使用して、ハンドルからオブジェクト名を取得
        private static string GetObjectName(IntPtr handle)
        {
            uint length = 0;
            // 必要なサイズを問い合わせる
            uint status = NtQueryObject(handle, ObjectNameInformation, IntPtr.Zero, 0, ref length);
            if (length == 0)
                return null;

            IntPtr nameInfoPtr = Marshal.AllocHGlobal((int)length);
            try
            {
                status = NtQueryObject(handle, ObjectNameInformation, nameInfoPtr, length, ref length);
                if (status != 0)
                    return null;

                // OBJECT_NAME_INFORMATION は先頭に UNICODE_STRING を持つ
                UNICODE_STRING unicodeStr = Marshal.PtrToStructure<UNICODE_STRING>(nameInfoPtr);
                if (unicodeStr.Length <= 0)
                    return null;
                // UNICODE_STRING の Buffer から文字列を取得
                return Marshal.PtrToStringUni(unicodeStr.Buffer, unicodeStr.Length / 2);
            }
            finally
            {
                Marshal.FreeHGlobal(nameInfoPtr);
            }
        }

        // UNICODE_STRING の構造体定義
        [StructLayout(LayoutKind.Sequential)]
        struct UNICODE_STRING
        {
            public ushort Length;
            public ushort MaximumLength;
            public IntPtr Buffer;
        }
    }
}