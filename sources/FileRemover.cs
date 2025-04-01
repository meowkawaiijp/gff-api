using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Goodbye_F__king_File
{
    public class FileRemover
    {
        private readonly string _FilePath;
        public string VerifyError { get; set; }

        public FileRemover(string fp)
        {
            _FilePath = fp;
        }

        // 指定されたパスが有効なファイルかどうかを検証
        public bool VerifyIfItsValid()
        {
            if (string.IsNullOrWhiteSpace(_FilePath))
            {
                VerifyError = "ファイルパスが指定されていません。";
                return false;
            }
            if (Directory.Exists(_FilePath))
            {
                VerifyError = "ディレクトリが指定されています。ファイルを指定してください。";
                return false;
            }
            if (!File.Exists(@"\\?\" + _FilePath))
            {
                VerifyError = "指定されたファイルが存在しません。";
                return false;
            }
            return true;
        }

        // 強制的にファイル削除を実行
        public void ForceRMFile()
        {
            Logger.Log(Logger.LogType.DEBUG, "==== ファイル削除処理を開始します ====");
            Logger.Log(Logger.LogType.DEBUG, "FilePath: " + _FilePath);

            // ファイル属性の確認と、読み取り専用属性であれば解除
            try
            {
                FileInfo file = new FileInfo(_FilePath);
                Logger.Log(Logger.LogType.DEBUG, "現在のファイル属性: " + file.Attributes);
                if ((file.Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                {
                    Logger.Log(Logger.LogType.WARN, "ファイルが読み取り専用属性のため、通常属性に変更します。");
                    file.Attributes = FileAttributes.Normal;
                    Logger.Log(Logger.LogType.DEBUG, "属性変更後のファイル属性: " + file.Attributes);
                }
                else
                {
                    Logger.Log(Logger.LogType.DEBUG, "ファイルは読み取り専用ではありません。");
                }
            }
            catch (Exception ex)
            {
                Logger.Log(Logger.LogType.ERROR, "ファイル属性の確認中にエラーが発生しました: " + ex.Message);
            }

            // QuickUnlink による初回削除試行
            Logger.Log(Logger.LogType.DEBUG, "QuickUnlink による削除試行を開始します...");
            Logger.LogNotNewLine(Logger.LogType.INFO, "QuickUnlink " + _FilePath + " ");
            int resultQuickUnlink = QuickUnlink();

            switch (resultQuickUnlink)
            {
                case 0:
                    Logger.LogNotNewLine_Next("-> [DONE]");
                    return;
                case 1:
                    Logger.LogNotNewLine_Next("-> [ArgumentException] パスが空文字、または空白のみ、または無効な文字を含んでいます。");
                    break;
                case 2:
                    Logger.LogNotNewLine_Next("-> [DirectoryNotFoundException] 指定されたパスは無効です。");
                    return;
                case 3:
                    Logger.LogNotNewLine_Next("-> [PathTooLongException] パス、ファイル名、またはその両方がシステム定義の最大長を超えています。");
                    break;
                case 4:
                    Logger.LogNotNewLine_Next("-> [IOException] 指定されたファイルは使用中です。");
                    break;
                case 5:
                    Logger.LogNotNewLine_Next("-> [NotSupportedException] パスの形式が無効です。");
                    break;
                case 6:
                    Logger.LogNotNewLine_Next("-> [UnauthorizedAccessException] 必要な権限がありません。または、実行中の実行可能ファイル、ディレクトリ、読み取り専用ファイルが指定されています。");
                    break;
                default:
                    Logger.LogNotNewLine_Next("-> [Exception] 不明なエラーが発生しました。");
                    Logger.Log(Logger.LogType.ERROR, "不明なエラーのためファイルを削除できません。");
                    return;
            }

            // 実行中のプロセスの場合、強制終了を試みる
            Logger.Log(Logger.LogType.INFO, "ファイルを使用中のプロセスを検索しています...");
            foreach (Process proc in Process.GetProcesses())
            {
                try
                {
                    if (proc.MainModule.FileName.Equals(_FilePath, StringComparison.OrdinalIgnoreCase))
                    {
                        Logger.Log(Logger.LogType.WARN, $"実行中のプロセス {proc.Id} ({proc.ProcessName}) がファイルを使用中です。強制終了を試みます...");
                        try
                        {
                            proc.Kill();
                            Logger.Log(Logger.LogType.INFO, $"プロセス {proc.Id} を正常に終了しました。");
                        }
                        catch
                        {
                            Logger.Log(Logger.LogType.WARN, $"プロセス {proc.Id} の終了に失敗したため、ProcessKiller を利用して強制終了を試みます...");
                            ProcessKiller.ForceKillProcess(proc);
                        }
                        try
                        {
                            proc.WaitForExit();
                        }
                        catch { }
                    }
                }
                catch { }
            }

            // ファイルをロックしているプロセスを検索
            Logger.Log(Logger.LogType.INFO, "ファイルをロックしているプロセスを取得しています...");
            try
            {
                List<Process> lockingProcesses = FileLockHelper.GetLockingProcesses(_FilePath);

                foreach (var proc in lockingProcesses)
                {
                    Logger.Log(Logger.LogType.WARN, $"プロセス {proc.Id} ({proc.ProcessName}) がファイルをロックしています。");

                    // まずはハンドル解放を試みる
                    bool handleClosed = FileLockHelper.ForceCloseFileHandle(proc, _FilePath);
                    if (!handleClosed)
                    {
                        Logger.Log(Logger.LogType.ERROR, $"ハンドルの解放に失敗しました。プロセス {proc.Id} を強制終了します。");
                        ProcessKiller.ForceKillProcess(proc);
                    }
                    else
                    {
                        Logger.Log(Logger.LogType.INFO, $"プロセス {proc.Id} のハンドルを強制的に解放しました。");
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Log(Logger.LogType.ERROR, "ファイルロックの取得に失敗しました。エラー: " + e.Message);
            }

            // 所有者/アクセス権限変更
            // TrustedInstaller の場合、SeTcbPrivilege があり全てバイパスできるため不要だが念のため実装
            try
            {
                FileInfo di = new FileInfo(_FilePath);
                FileSecurity ds = di.GetAccessControl();

                // TrustedInstaller の NTAccount を取得
                var trustedInstaller = new NTAccount("NT SERVICE", "TrustedInstaller");

                // TrustedInstaller の削除権限が既にあるか確認する
                bool hasDeletePermission = false;
                AuthorizationRuleCollection rules = ds.GetAccessRules(true, true, typeof(NTAccount));
                foreach (FileSystemAccessRule rule in rules)
                {
                    // ルールの対象がTrustedInstallerか確認
                    if (rule.IdentityReference.Value.Equals(trustedInstaller.Value, StringComparison.OrdinalIgnoreCase))
                    {
                        // 削除権限が許可されているかチェック
                        if ((rule.FileSystemRights & FileSystemRights.Delete) == FileSystemRights.Delete &&
                            rule.AccessControlType == AccessControlType.Allow)
                        {
                            hasDeletePermission = true;
                            break;
                        }
                    }
                }

                // TrustedInstallerに削除権限がなかった場合のみ、所有者の変更とフルコントロールの付与を行う
                if (!hasDeletePermission)
                {
                    // 所有者の設定
                    ds.SetOwner(trustedInstaller);

                    // TrustedInstaller にフルコントロールを付与
                    var accessRule = new FileSystemAccessRule(
                        trustedInstaller,
                        FileSystemRights.FullControl,
                        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                        PropagationFlags.None,
                        AccessControlType.Allow);
                    ds.AddAccessRule(accessRule);

                    di.SetAccessControl(ds);
                    Logger.Log(Logger.LogType.INFO, "ファイルの所有者および権限を TrustedInstaller に修正しました。");
                }
                else
                {
                    Logger.Log(Logger.LogType.INFO, "TrustedInstaller は既に削除権限を有しています。");
                }
            }
            catch (Exception e)
            {
                Logger.Log(Logger.LogType.ERROR, $"ファイルの権限修正に失敗しました。エラー: {e.Message}");
            }


            // DOS Device Path を利用してファイル削除を再試行
            Logger.Log(Logger.LogType.INFO, "DOS Device Path を利用して最終的なファイル削除を試行します...");
            try
            {
                File.Delete(@"\\?\" + _FilePath);
                Logger.Log(Logger.LogType.INFO, "最終的にファイルの削除に成功しました。");
            }
            catch (Exception ex)
            {
                Logger.Log(Logger.LogType.ERROR, "最終的な削除に失敗しました。エラー: " + ex.Message);
            }
        }

        int QuickUnlink()
        {
            try
            {
                File.Delete(_FilePath);
            }
            catch (ArgumentException)
            {
                return 1;
            }
            catch (DirectoryNotFoundException)
            {
                return 2;
            }
            catch (PathTooLongException)
            {
                return 3;
            }
            catch (IOException)
            {
                return 4;
            }
            catch (NotSupportedException)
            {
                return 5;
            }
            catch (UnauthorizedAccessException)
            {
                return 6;
            }
            catch (Exception)
            {
                return -1;
            }
            return 0;
        }
    }
}