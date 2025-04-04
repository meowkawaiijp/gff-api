using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Goodbye_F__king_File
{
    class IrreversibleOverrideHandler
    {
        #region 定数と構造体

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

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern bool GetVolumeInformation(
            string lpRootPathName,
            StringBuilder lpVolumeNameBuffer,
            int nVolumeNameSize,
            out uint lpVolumeSerialNumber,
            out uint lpMaximumComponentLength,
            out uint lpFileSystemFlags,
            StringBuilder lpFileSystemNameBuffer,
            int nFileSystemNameSize
        );

        #endregion

        // ファイル内容の複数回上書きとか、MFTのゼロ埋めとか！
        public static int IrreversibleQuickUnlink(string filePath)
        {
            // ドライブ名抽出
            string drive = Path.GetPathRoot(Path.GetFullPath(filePath));
            Logger.Log(Logger.LogType.DEBUG, $"ファイルパスからドライブ '{drive}' を抽出しました。");
            if (string.IsNullOrEmpty(drive))
            {
                Logger.Log(Logger.LogType.ERROR, "ドライブの抽出に失敗しました。");
                return 5;
            }

            // ボリューム情報取得
            StringBuilder fsName = new StringBuilder(16);
            Logger.Log(Logger.LogType.DEBUG, $"ドライブ '{drive}' のボリューム情報を取得しています。");
            if (!GetVolumeInformation(
                    drive,
                    new StringBuilder(256), 256,
                    out uint volSerial,
                    out uint maxComponent,
                    out uint fsFlags,
                    fsName, fsName.Capacity))
            {
                Logger.Log(Logger.LogType.ERROR, "GetVolumeInformation の呼び出しに失敗しました。");
                return 7;
            }
            string fileSystem = fsName.ToString().ToUpperInvariant();
            Logger.Log(Logger.LogType.DEBUG, $"ファイルシステムの種類: '{fileSystem}'");

            if (fileSystem != "NTFS")
            {
                Logger.Log(Logger.LogType.WARN, $"サポートされていないファイルシステムです: '{fileSystem}'");
                return 7;
            }

            // ファイル内容上書き
            try
            {   
                Logger.Log(Logger.LogType.DEBUG, "ファイル内容の上書きを開始します(0x00/0xFF/Rand)。");
                FileInfo fi = new FileInfo(filePath);
                long length = fi.Length;
                Logger.Log(Logger.LogType.DEBUG, $"ファイルサイズ: {length} bytes");
                const int sectorSize = 4096;
                using (SafeFileHandle fileHandle = CreateFile(
                    filePath,
                    EFileAccess.GENERIC_WRITE,
                    EFileShare.None,
                    IntPtr.Zero,
                    ECreationDisposition.OPEN_EXISTING,
                    EFileAttributes.FILE_FLAG_WRITE_THROUGH | EFileAttributes.FILE_FLAG_NO_BUFFERING,
                    IntPtr.Zero))
                {
                    if (fileHandle.IsInvalid)
                    {
                        Logger.Log(Logger.LogType.ERROR, "書き込み用のファイルハンドラー取得に失敗しました。");
                        return 7;
                    }
                    Logger.Log(Logger.LogType.DEBUG, "書き込み用のファイルハンドラーを作成しました。");
                    using (FileStream fs = new FileStream(fileHandle, FileAccess.Write))
                    {
                        byte[] buffer = new byte[sectorSize];
                        byte[] randomBuffer = new byte[sectorSize];
                        Random rng = new Random();
                        for (int pass = 0; pass < 3; pass++)
                        {
                            Logger.Log(Logger.LogType.DEBUG, $"{pass}回目の上書きを開始しています...。");
                            fs.Seek(0, SeekOrigin.Begin);
                            long bytesRemaining = length;
                            int fullChunks = (int)(bytesRemaining / sectorSize);
                            int remainder = (int)(bytesRemaining % sectorSize);
                            for (int i = 0; i < fullChunks; i++)
                            {
                                switch (pass)
                                {
                                    case 0:
                                        Array.Clear(buffer, 0, sectorSize);
                                        fs.Write(buffer, 0, sectorSize);
                                        break;
                                    case 1:
                                        for (int j = 0; j < sectorSize; j++)
                                            buffer[j] = 0xFF;
                                        fs.Write(buffer, 0, sectorSize);
                                        break;
                                    case 2:
                                        rng.NextBytes(randomBuffer);
                                        fs.Write(randomBuffer, 0, sectorSize);
                                        break;
                                }
                            }
                            if (remainder > 0)
                            {
                                switch (pass)
                                {
                                    case 0:
                                        Array.Clear(buffer, 0, sectorSize);
                                        break;
                                    case 1:
                                        for (int j = 0; j < sectorSize; j++)
                                            buffer[j] = 0xFF;
                                        break;
                                    case 2:
                                        rng.NextBytes(randomBuffer);
                                        break;
                                }
                                fs.Write(pass == 2 ? randomBuffer : buffer, 0, sectorSize);
                            }
                            fs.Flush(true);
                            // 不要 (セクター単位での書き込みによりファイルサイズが増えてるはずなので、元のサイズに切り詰める処理)
                            // まあ別にファイルを消したいだけなので...。
                            // fs.SetLength(length);
                        }
                    }
                }
                Logger.Log(Logger.LogType.DEBUG, "ファイルの上書き処理が完了しました。");
            }
            catch (Exception ex)
            {
                Logger.Log(Logger.LogType.ERROR, $"ファイル上書きの途中でエラーが発生しました: {ex.Message}");
                return -1;
            }
            // NTFSの場合は MFT ファイルレコードのゼロ化
            if (fileSystem == "NTFS")
            {
                Logger.Log(Logger.LogType.DEBUG, "NTFSファイルシステムを検出しました。MFTファイルレコードのゼロ上書きを開始します。");
                int ret = NTFSMFTEraser.ZeroNTFSFileRecord(filePath, drive);
                if (ret != 0)
                {
                    Logger.Log(Logger.LogType.WARN, $"ZeroNTFSFileRecord がエラーコード {ret} を返しました。");
                    return ret;
                }
            }
            else
            {
                return -1;
            }

            Logger.Log(Logger.LogType.DEBUG, "IrreversibleQuickUnlink が正常に完了しました。");
            return 0;
        }

    }
}
