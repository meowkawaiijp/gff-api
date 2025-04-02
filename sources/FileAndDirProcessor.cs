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
            Logger.Log(Logger.LogType.DEBUG, $"RemoveUsingfileRemover => filePath = '{filePath}'");

            // ファイルの場合は直接削除
            if (!Directory.Exists(filePath))
            {
                Logger.Log(Logger.LogType.DEBUG, $"'{filePath}' はファイルとして認識されました。FileRemover インスタンスを生成します。");
                var fileRemover = new FileRemover(filePath);
                Logger.Log(Logger.LogType.DEBUG, $"FileRemover を生成しました。次にファイルの有効性を検証します。");
                if (fileRemover.VerifyIfItsValid())
                {
                    Logger.Log(Logger.LogType.DEBUG, $"ファイル '{filePath}' の検証に成功。削除処理を実行します。");
                    fileRemover.ForceRMFile();
                    Logger.Log(Logger.LogType.DEBUG, $"ForceRMFile() の実行が完了しました。");
                }
                else
                {
                    Logger.Log(Logger.LogType.DEBUG, $"ファイル '{filePath}' の検証に失敗。エラー内容をログ出力します。");
                    Logger.Log(Logger.LogType.ERROR, "エラー: " + fileRemover.VerifyError);

                    if (Logger.ShowDebug)
                    {
                        Logger.Log(Logger.LogType.INFO, "デバッグモードが有効化されているため、エラーを無視して処理を続行します。");
                        fileRemover.ForceRMFile();
                        Logger.Log(Logger.LogType.DEBUG, $"ForceRMFile() の実行が完了しました。");
                    }
                }
                return;
            }

            Logger.Log(Logger.LogType.DEBUG, $"'{filePath}' はディレクトリとして認識されました。ディレクトリ内のファイルを取得します。");
            // ディレクトリの場合は、まず直下のファイルを削除
            string[] files = GetFilesInDirectory(filePath);
            Logger.Log(Logger.LogType.DEBUG, $"GetFilesInDirectory 結果: {FormatArrayForLog(files)}");
            foreach (string file in files)
            {
                Logger.Log(Logger.LogType.DEBUG, $"ディレクトリ内のファイルを再帰的に処理します: '{file}'");
                // 再帰呼び出し（ファイルの場合は上記処理が実行される）
                RemoveUsingfileRemover(file);
            }

            Logger.Log(Logger.LogType.DEBUG, $"ディレクトリ '{filePath}' のサブディレクトリ一覧を取得します。");
            // 次に、サブディレクトリを再帰的に処理
            string[] subDirectories = GetDirectoriesInDirectory(filePath);
            Logger.Log(Logger.LogType.DEBUG, $"GetDirectoriesInDirectory 結果: {FormatArrayForLog(subDirectories)}");
            foreach (string subDir in subDirectories)
            {
                Logger.Log(Logger.LogType.DEBUG, $"サブディレクトリを再帰的に処理します: '{subDir}'");
                RemoveUsingfileRemover(subDir);
            }

            // 全ての中身が削除できたので、現在のディレクトリを削除
            Logger.Log(Logger.LogType.DEBUG, $"全ての中身の削除が完了。ディレクトリ '{filePath}' の削除を試みます。");
            try
            {
                Directory.Delete(filePath, false);
                Logger.Log(Logger.LogType.DEBUG, $"ディレクトリ '{filePath}' の削除に成功しました。");
            }
            catch (Exception e)
            {
                Logger.Log(Logger.LogType.ERROR, $"ディレクトリ '{filePath}' の削除に失敗しました。エラー: {e.Message}");
                Logger.Log(Logger.LogType.DEBUG, $"例外詳細: {e}");
            }
        }

        // 指定したディレクトリ直下のファイル一覧を取得する
        private static string[] GetFilesInDirectory(string path)
        {
            Logger.Log(Logger.LogType.DEBUG, $"GetFilesInDirectory 開始: path = '{path}'");
            try
            {
                string[] files = Directory.GetFiles(path, "*");
                Logger.Log(Logger.LogType.DEBUG, $"ディレクトリ '{path}' のファイル取得に成功。取得件数: {files.Length}. 内容: {FormatArrayForLog(files)}");
                return files;
            }
            catch (UnauthorizedAccessException ex)
            {
                Logger.Log(Logger.LogType.ERROR, $"ディレクトリ '{path}' のファイル取得にアクセス拒否が発生しました。エラー: {ex.Message}");
                Logger.Log(Logger.LogType.DEBUG, $"アクセス拒否例外発生。権限修正を試みます。 path = '{path}'");
                FixDirectoryPermissions(path);
                try
                {
                    string[] files = Directory.GetFiles(path, "*");
                    Logger.Log(Logger.LogType.DEBUG, $"権限修正後、ディレクトリ '{path}' のファイル取得に成功。取得件数: {files.Length}. 内容: {FormatArrayForLog(files)}");
                    return files;
                }
                catch (Exception ex2)
                {
                    Logger.Log(Logger.LogType.ERROR, $"権限修正後もディレクトリ '{path}' のファイル取得に失敗しました。エラー: {ex2.Message}");
                    Logger.Log(Logger.LogType.DEBUG, $"再試行失敗。例外詳細: {ex2}");
                    return new string[0];
                }
            }
            catch (Exception ex)
            {
                Logger.Log(Logger.LogType.ERROR, $"ディレクトリ '{path}' のファイル取得に失敗しました。エラー: {ex.Message}");
                Logger.Log(Logger.LogType.DEBUG, $"一般例外発生。例外詳細: {ex}");
                return new string[0];
            }
        }

        // 指定したディレクトリ直下のサブディレクトリ一覧を取得する
        private static string[] GetDirectoriesInDirectory(string path)
        {
            Logger.Log(Logger.LogType.DEBUG, $"GetDirectoriesInDirectory 開始: path = '{path}'");
            try
            {
                string[] directories = Directory.GetDirectories(path);
                Logger.Log(Logger.LogType.DEBUG, $"ディレクトリ '{path}' のサブディレクトリ取得に成功。件数: {directories.Length} / 内容: {FormatArrayForLog(directories)}");
                return directories;
            }
            catch (UnauthorizedAccessException ex)
            {
                Logger.Log(Logger.LogType.ERROR, $"ディレクトリ '{path}' のサブディレクトリ取得にアクセス拒否が発生しました。エラー: {ex.Message}");
                Logger.Log(Logger.LogType.DEBUG, $"アクセス拒否例外発生。権限修正を試みます。 path = '{path}'");
                FixDirectoryPermissions(path);
                try
                {
                    string[] directories = Directory.GetDirectories(path);
                    Logger.Log(Logger.LogType.DEBUG, $"権限修正後、ディレクトリ '{path}' のサブディレクトリ取得に成功。件数: {directories.Length} / 内容: {FormatArrayForLog(directories)}");
                    return directories;
                }
                catch (Exception ex2)
                {
                    Logger.Log(Logger.LogType.ERROR, $"権限修正後もディレクトリ '{path}' のサブディレクトリ取得に失敗しました。エラー: {ex2.Message}");
                    Logger.Log(Logger.LogType.DEBUG, $"再試行失敗。例外詳細: {ex2}");
                    return new string[0];
                }
            }
            catch (Exception ex)
            {
                Logger.Log(Logger.LogType.ERROR, $"ディレクトリ '{path}' のサブディレクトリ取得に失敗しました。エラー: {ex.Message}");
                Logger.Log(Logger.LogType.DEBUG, $"一般例外発生。例外詳細: {ex}");
                return new string[0];
            }
        }

        // TrustedInstaller にフルコントロールのアクセス権を付与
        private static void FixDirectoryPermissions(string path)
        {
            Logger.Log(Logger.LogType.DEBUG, $"FixDirectoryPermissions 開始: path = '{path}'");
            try
            {
                DirectoryInfo di = new DirectoryInfo(path);
                Logger.Log(Logger.LogType.DEBUG, $"DirectoryInfo を作成しました: '{path}'");
                DirectorySecurity ds = di.GetAccessControl();
                Logger.Log(Logger.LogType.DEBUG, $"DirectorySecurity を取得しました: '{path}'");

                // TrustedInstaller の NTAccount を取得
                var trustedInstaller = new NTAccount("NT SERVICE", "TrustedInstaller");
                Logger.Log(Logger.LogType.DEBUG, $"NTAccount (TrustedInstaller) を生成しました。");

                // 所有者の設定
                ds.SetOwner(trustedInstaller);
                Logger.Log(Logger.LogType.DEBUG, $"所有者を TrustedInstaller に設定しました。");

                // TrustedInstaller にフルコントロールを付与
                var accessRule = new FileSystemAccessRule(
                    trustedInstaller,
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow);

                ds.AddAccessRule(accessRule);
                Logger.Log(Logger.LogType.DEBUG, $"TrustedInstaller にフルコントロール権限を付与するルールを追加しました。");

                di.SetAccessControl(ds);
                Logger.Log(Logger.LogType.INFO, $"ディレクトリ '{path}' の所有者および権限を TrustedInstaller に修正しました。");
                Logger.Log(Logger.LogType.DEBUG, $"FixDirectoryPermissions 処理が正常に完了しました: '{path}'");
            }
            catch (Exception e)
            {
                Logger.Log(Logger.LogType.ERROR, $"ディレクトリ '{path}' の権限修正に失敗しました。エラー: {e.Message}");
                Logger.Log(Logger.LogType.DEBUG, $"FixDirectoryPermissions 例外詳細: {e}");
            }
        }

        // 配列の内容をログ出力用にフォーマット
        private static string FormatArrayForLog(string[] array)
        {
            if (array == null)
            {
                return "null";
            }
            int maxItems = 5;
            string result = "[";
            for (int i = 0; i < array.Length && i < maxItems; i++)
            {
                result += $"'{array[i]}'";
                if (i < array.Length - 1 && i < maxItems - 1)
                {
                    result += ", ";
                }
            }
            if (array.Length > maxItems)
            {
                result += $", ...（{array.Length - maxItems} 件省略）";
            }
            result += "]";
            return result;
        }
    }
}
