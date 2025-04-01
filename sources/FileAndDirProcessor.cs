using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System;

namespace Goodbye_F__king_File
{
    class FileAndDirectoryProcessor
    {
        public static void RemoveUsingfileRemover(string filePath)
        {
            // ファイルの場合は直接削除
            if (!Directory.Exists(filePath))
            {
                var fileRemover = new FileRemover(filePath);
                if (fileRemover.VerifyIfItsValid())
                {
                    fileRemover.ForceRMFile();
                }
                else
                {
                    Logger.Log(Logger.LogType.ERROR, "エラー: " + fileRemover.VerifyError);
                }
                return;
            }

            // ディレクトリの場合は、まず直下のファイルを削除
            string[] files = GetFilesInDirectory(filePath);
            foreach (string file in files)
            {
                // 再帰呼び出し（ファイルの場合は上記処理が実行される）
                RemoveUsingfileRemover(file);
            }

            // 次に、サブディレクトリを再帰的に処理
            string[] subDirectories = GetDirectoriesInDirectory(filePath);
            foreach (string subDir in subDirectories)
            {
                RemoveUsingfileRemover(subDir);
            }

            // 全ての中身が削除できたので、現在のディレクトリを削除
            try
            {
                Directory.Delete(filePath, false);
            }
            catch (Exception e)
            {
                Logger.Log(Logger.LogType.ERROR, $"ディレクトリ '{filePath}' の削除に失敗しました。エラー: {e.Message}");
            }
        }

        // 指定したディレクトリ直下のファイル一覧を取得する
        private static string[] GetFilesInDirectory(string path)
        {
            try
            {
                return Directory.GetFiles(path, "*");
            }
            catch (UnauthorizedAccessException ex)
            {
                Logger.Log(Logger.LogType.ERROR, $"ディレクトリ '{path}' のファイル取得にアクセス拒否が発生しました。エラー: {ex.Message}");
                FixDirectoryPermissions(path);
                try
                {
                    return Directory.GetFiles(path, "*");
                }
                catch (Exception ex2)
                {
                    Logger.Log(Logger.LogType.ERROR, $"権限修正後もディレクトリ '{path}' のファイル取得に失敗しました。エラー: {ex2.Message}");
                    return new string[0];
                }
            }
            catch (Exception ex)
            {
                Logger.Log(Logger.LogType.ERROR, $"ディレクトリ '{path}' のファイル取得に失敗しました。エラー: {ex.Message}");
                return new string[0];
            }
        }

        // 指定したディレクトリ直下のサブディレクトリ一覧を取得する
        private static string[] GetDirectoriesInDirectory(string path)
        {
            try
            {
                return Directory.GetDirectories(path);
            }
            catch (UnauthorizedAccessException ex)
            {
                Logger.Log(Logger.LogType.ERROR, $"ディレクトリ '{path}' のサブディレクトリ取得にアクセス拒否が発生しました。エラー: {ex.Message}");
                FixDirectoryPermissions(path);
                try
                {
                    return Directory.GetDirectories(path);
                }
                catch (Exception ex2)
                {
                    Logger.Log(Logger.LogType.ERROR, $"権限修正後もディレクトリ '{path}' のサブディレクトリ取得に失敗しました。エラー: {ex2.Message}");
                    return new string[0];
                }
            }
            catch (Exception ex)
            {
                Logger.Log(Logger.LogType.ERROR, $"ディレクトリ '{path}' のサブディレクトリ取得に失敗しました。エラー: {ex.Message}");
                return new string[0];
            }
        }

        // TrustedInstaller にフルコントロールのアクセス権を付与
        private static void FixDirectoryPermissions(string path)
        {
            try
            {
                DirectoryInfo di = new DirectoryInfo(path);
                DirectorySecurity ds = di.GetAccessControl();

                // TrustedInstaller の NTAccount を取得
                var trustedInstaller = new NTAccount("NT SERVICE", "TrustedInstaller");

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
                Logger.Log(Logger.LogType.INFO, $"ディレクトリ '{path}' の所有者および権限を TrustedInstaller に修正しました。");
            }
            catch (Exception e)
            {
                Logger.Log(Logger.LogType.ERROR, $"ディレクトリ '{path}' の権限修正に失敗しました。エラー: {e.Message}");
            }
        }
    }
}