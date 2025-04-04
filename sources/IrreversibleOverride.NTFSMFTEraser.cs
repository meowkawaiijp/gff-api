using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;

namespace Goodbye_F__king_File
{
    class NTFSMFTEraser
    {
        #region 定数と構造体

        // FSCTLコード
        private const uint FSCTL_GET_NTFS_VOLUME_DATA = 0x00090064;

        // NTFS_VOLUME_DATA_BUFFER 構造体（NTFSボリューム情報）
        [StructLayout(LayoutKind.Sequential)]
        public struct NTFS_VOLUME_DATA_BUFFER
        {
            public long VolumeSerialNumber;
            public long NumberSectors;
            public long TotalClusters;
            public long FreeClusters;
            public long TotalReserved;
            public uint BytesPerSector;
            public uint BytesPerCluster;
            public uint BytesPerFileRecordSegment;
            public uint ClustersPerFileRecordSegment;
            public long MftValidDataLength;
            public long MftStartLcn;
            public long Mft2StartLcn;
            public long MftZoneStart;
            public long MftZoneEnd;
        }
        // BY_HANDLE_FILE_INFORMATION 構造体（ファイルインデックス取得用）
        [StructLayout(LayoutKind.Sequential)]
        public struct BY_HANDLE_FILE_INFORMATION
        {
            public uint FileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
            public uint VolumeSerialNumber;
            public uint FileSizeHigh;
            public uint FileSizeLow;
            public uint NumberOfLinks;
            public uint FileIndexHigh;
            public uint FileIndexLow;
        }

        // CreateFile用定義
        [Flags]
        private enum EFileAccess : uint
        {
            GENERIC_READ = 0x80000000,
            GENERIC_WRITE = 0x40000000
        }

        [Flags]
        private enum EFileShare : uint
        {
            None = 0x00000000,
            Read = 0x00000001,
            Write = 0x00000002,
            Delete = 0x00000004
        }

        private enum ECreationDisposition : uint
        {
            OPEN_EXISTING = 3
        }

        [Flags]
        private enum EFileAttributes : uint
        {
            FILE_FLAG_WRITE_THROUGH = 0x80000000,
            FILE_FLAG_NO_BUFFERING = 0x20000000,
            FILE_FLAG_BACKUP_SEMANTICS = 0x02000000
        }

        #endregion

        #region P/Invoke定義

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern SafeFileHandle CreateFile(
            string lpFileName,
            EFileAccess dwDesiredAccess,
            EFileShare dwShareMode,
            IntPtr lpSecurityAttributes,
            ECreationDisposition dwCreationDisposition,
            EFileAttributes dwFlagsAndAttributes,
            IntPtr hTemplateFile
        );

        // ボリューム情報取得用
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool DeviceIoControl(
            SafeFileHandle hDevice,
            uint dwIoControlCode,
            IntPtr lpInBuffer,
            int nInBufferSize,
            out NTFS_VOLUME_DATA_BUFFER lpOutBuffer,
            int nOutBufferSize,
            out int lpBytesReturned,
            IntPtr lpOverlapped
        );

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetFileInformationByHandle(SafeFileHandle hFile, out BY_HANDLE_FILE_INFORMATION lpFileInformation);

        // WriteFileのP/Invoke宣言
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteFile(
            SafeFileHandle hFile,
            IntPtr lpBuffer,
            int nNumberOfBytesToWrite,
            out int lpNumberOfBytesWritten,
            ref NativeOverlapped lpOverlapped);

        #endregion

        private static string GetLastErrorMessage()
        {
            int errorCode = Marshal.GetLastWin32Error();
            return new Win32Exception(errorCode).Message + $" (Error Code: {errorCode})";
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct MftRecordHeader
        {
            public uint Magic;
            public ushort UpdateSeqOffset;
            public ushort UpdateSeqSize;
            public ulong LogFileSequenceNumber;
            public ushort SequenceNumber;
            public ushort HardLinkCount;
            public ushort FirstAttributeOffset;
            public ushort Flags;
            public uint UsedSizeOfMFT;
            public uint AllocatedSizeOfMFT;
            public ulong FileReferenceToBase;
            public ushort NextAttributeID;
            public ushort AlignToWordBoundary;
            public uint MftRecordNumber;
        }

        public static int ZeroNTFSFileRecord(string filePath, string drive)
        {
            Logger.Log(Logger.LogType.DEBUG, "ZeroNTFSFileRecord の処理を開始しました。");
            try
            {
                using (SafeFileHandle hFile = CreateFile(
                    filePath,
                    EFileAccess.GENERIC_READ | EFileAccess.GENERIC_WRITE,
                    EFileShare.Read | EFileShare.Write | EFileShare.Delete,
                    IntPtr.Zero,
                    ECreationDisposition.OPEN_EXISTING,
                    0,
                    IntPtr.Zero))
                {
                    if (hFile.IsInvalid)
                    {
                        Logger.Log(Logger.LogType.ERROR, "ファイル情報取得のための CreateFile 呼び出しに失敗しました。");
                        return 7;
                    }
                    Logger.Log(Logger.LogType.DEBUG, "ファイル情報取得用のファイルハンドルを取得しました。");
                    if (!GetFileInformationByHandle(hFile, out BY_HANDLE_FILE_INFORMATION fileInfo))
                    {
                        Logger.Log(Logger.LogType.ERROR, "GetFileInformationByHandle の呼び出しに失敗しました。");
                        return 7;
                    }
                    ulong rawFileIndex = (((ulong)fileInfo.FileIndexHigh) << 32) | fileInfo.FileIndexLow;
                    ulong mftRecordNumber = rawFileIndex & 0x0000FFFFFFFFFFFF;
                    Logger.Log(Logger.LogType.DEBUG, $"ファイルレコード番号を決定しました: {mftRecordNumber}");

                    string volPath = @"\\.\" + drive.TrimEnd('\\');
                    Logger.Log(Logger.LogType.DEBUG, $"ボリューム '{volPath}' をRAWアクセス用に開いています...。");
                    using (SafeFileHandle hVolume = CreateFile(
                        volPath,
                        EFileAccess.GENERIC_READ | EFileAccess.GENERIC_WRITE,
                        EFileShare.Read | EFileShare.Write | EFileShare.Delete,
                        IntPtr.Zero,
                        ECreationDisposition.OPEN_EXISTING,
                        EFileAttributes.FILE_FLAG_NO_BUFFERING | EFileAttributes.FILE_FLAG_WRITE_THROUGH | EFileAttributes.FILE_FLAG_BACKUP_SEMANTICS,
                        IntPtr.Zero))
                    {
                        if (hVolume.IsInvalid)
                        {
                            Logger.Log(Logger.LogType.ERROR, "ボリュームハンドルの取得に失敗しました。");
                            return 7;
                        }
                        Logger.Log(Logger.LogType.DEBUG, "ボリュームハンドルを取得しました。");

                        Logger.Log(Logger.LogType.DEBUG, "NTFSボリュームデータを取得しています。");
                        if (!DeviceIoControl(
                                hVolume,
                                FSCTL_GET_NTFS_VOLUME_DATA,
                                IntPtr.Zero, 0,
                                out NTFS_VOLUME_DATA_BUFFER volData,
                                Marshal.SizeOf(typeof(NTFS_VOLUME_DATA_BUFFER)),
                                out int bytesReturned,
                                IntPtr.Zero))
                        {
                            Logger.Log(Logger.LogType.ERROR,
                                "DeviceIoControl により NTFSボリュームデータの取得に失敗しました。 " + GetLastErrorMessage());
                            return 7;
                        }

                        Logger.Log(Logger.LogType.DEBUG,
                            $"NTFSボリュームデータを取得しました。 MFTの開始LCN: {volData.MftStartLcn}、" +
                            $"BytesPerSector: {volData.BytesPerSector}、" +
                            $"BytesPerCluster: {volData.BytesPerCluster}、" +
                            $"BytesPerFileRecordSegment: {volData.BytesPerFileRecordSegment}"
                        );

                        long mftStartOffset = volData.MftStartLcn * volData.BytesPerCluster;
                        long recordSize = volData.BytesPerFileRecordSegment;
                        long recordOffset = mftStartOffset + ((long)mftRecordNumber * recordSize);

                        Logger.Log(Logger.LogType.DEBUG,
                            $"ファイルレコード座標の計算結果: 開始位置={recordOffset} / サイズ={recordSize}");

                        bool canRead = ReadAndParseMftRecord(hVolume, recordOffset, (int)recordSize);
                        if (!canRead)
                        {
                            Logger.Log(Logger.LogType.ERROR, "MFTレコードの読み取りまたは解析に失敗しました。");
                            return 7;
                        }
                        else
                        {
                            Logger.Log(Logger.LogType.DEBUG, "MFTレコードの読み取りに成功しました。");
                        }

                        Logger.Log(Logger.LogType.DEBUG, "MFTレコード領域のゼロ化を開始します。");

                        // ボリュームのロック
                        // は流石に動かない。他のプロセスもハンドルを無数に握ってるし無理か...()
                        /*
                        [DllImport("kernel32.dll", SetLastError = true)]
                        private static extern bool DeviceIoControl(
                          SafeFileHandle hDevice,
                            uint dwIoControlCode,
                            IntPtr lpInBuffer,
                            uint nInBufferSize,
                            IntPtr lpOutBuffer,
                            uint nOutBufferSize,
                            out uint lpBytesReturned,
                            IntPtr lpOverlapped
                        );
                        private const uint FSCTL_LOCK_VOLUME = 0x00090018;

                        uint bytesReturned2;
                        bool lockResult = DeviceIoControl(hVolume, FSCTL_LOCK_VOLUME, IntPtr.Zero, 0, IntPtr.Zero, 0, out bytesReturned2, IntPtr.Zero);
                        if (!lockResult)
                        {
                            Logger.Log(Logger.LogType.ERROR, "DeviceIoControl->FSCTL_LOCK_VOLUME に失敗しました: " + new Win32Exception(Marshal.GetLastWin32Error()).Message);
                            return 7;
                        }                        
                        */

                        /* FileStream だとダメらしい。WriteFile を使う。
                        using (FileStream volumeStream = new FileStream(hVolume, FileAccess.Write, 4096))
                        {
                            volumeStream.Seek(recordOffset, SeekOrigin.Begin);
                            byte[] zeros = new byte[recordSize];
                            volumeStream.Write(zeros, 0, (int)recordSize);
                            volumeStream.Flush();
                        }
                        */

                        // ゼロで埋めたバッファを用意
                        byte[] zeros = new byte[recordSize];
                        int bytesWritten;

                        // OVERLAPPED構造体にオフセット情報を設定
                        NativeOverlapped overlapped = new NativeOverlapped
                        {
                            OffsetLow = (int)(recordOffset & 0xFFFFFFFF),
                            OffsetHigh = (int)(recordOffset >> 32)
                        };

                        // ゼロバッファのポインタを固定してWriteFileを呼び出す
                        unsafe
                        {
                            fixed (byte* pZeros = zeros)
                            {
                                bool result = WriteFile(hVolume, (IntPtr)pZeros, zeros.Length, out bytesWritten, ref overlapped);
                                if (!result)
                                {
                                    Logger.Log(Logger.LogType.ERROR, "WriteFile に失敗しました: " + new Win32Exception(Marshal.GetLastWin32Error()).Message);
                                    Program.ShowWhoamiIfDebug();
                                    return 7;
                                }
                            }
                        }

                        Logger.Log(Logger.LogType.DEBUG, "MFTレコードのゼロ化に成功しました。");
                    }
                }
            }
            catch (Exception ex)
            {
                // 呼び出し元で WARN になるので LogType は DEUBG にしておく！
                Logger.Log(Logger.LogType.DEBUG, $"ZeroNTFSFileRecord 内で例外が発生しました: {ex.Message} ");
                Program.ShowWhoamiIfDebug();
                return -1;
            }
            Logger.Log(Logger.LogType.DEBUG, "ZeroNTFSFileRecord を正常に終了します。");
            return 0;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool DuplicateHandle(
            IntPtr hSourceProcessHandle,
            IntPtr hSourceHandle,
            IntPtr hTargetProcessHandle,
            out IntPtr lpTargetHandle,
            uint dwDesiredAccess,
            bool bInheritHandle,
            uint dwOptions);

        private const uint DUPLICATE_SAME_ACCESS = 0x00000002;

        private static bool ReadAndParseMftRecord(SafeFileHandle volumeHandle, long offset, int recordSize)
        {
            try
            {
                IntPtr currentProcess = Process.GetCurrentProcess().Handle;
                if (!DuplicateHandle(
                        currentProcess,
                        volumeHandle.DangerousGetHandle(),
                        currentProcess,
                        out IntPtr dupHandle,
                        0,
                        false,
                        DUPLICATE_SAME_ACCESS))
                {
                    Logger.Log(Logger.LogType.ERROR, "DuplicateHandle に失敗しました: " + new Win32Exception(Marshal.GetLastWin32Error()).Message);
                    return false;
                }
                using (SafeFileHandle dupSafeHandle = new SafeFileHandle(dupHandle, true))
                {
                    using (FileStream fs = new FileStream(dupSafeHandle, FileAccess.Read, 4096))
                    {
                        fs.Seek(offset, SeekOrigin.Begin);
                        byte[] rawData = new byte[recordSize];
                        int readBytes = fs.Read(rawData, 0, recordSize);
                        if (readBytes < recordSize)
                        {
                            Logger.Log(Logger.LogType.ERROR,
                                $"完全なMFTファイルレコードの読み取りに失敗しました。読み取ったバイト数={readBytes}/{recordSize}");
                            return false;
                        }
                        GCHandle pinnedArray = GCHandle.Alloc(rawData, GCHandleType.Pinned);
                        try
                        {
                            IntPtr pData = pinnedArray.AddrOfPinnedObject();
                            MftRecordHeader header = Marshal.PtrToStructure<MftRecordHeader>(pData);
                            if (header.Magic != 0x454C4946)
                            {
                                Logger.Log(Logger.LogType.WARN,
                                    $"MFTファイルレコードのマジック値が一致しません: 0x{header.Magic:X8} != 0x454C4946 (FILE)。");
                                return false;
                            }
                            Logger.Log(Logger.LogType.DEBUG,
                                $"【MFTファイルレコードの解析】Magic='FILE' " +
                                $"SeqNo={header.SequenceNumber}, HardLinks={header.HardLinkCount}, " +
                                $"FirstAttrOff=0x{header.FirstAttributeOffset:X}, Flags=0x{header.Flags:X}, " +
                                $"UsedSize={header.UsedSizeOfMFT} / AllocSize={header.AllocatedSizeOfMFT}"
                            );
                        }
                        finally
                        {
                            pinnedArray.Free();
                        }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log(Logger.LogType.ERROR,
                    $"ReadAndParseMftRecord 内で例外が発生しました: {ex.Message}; {ex.StackTrace}");
                return false;
            }
        }
    }
}
