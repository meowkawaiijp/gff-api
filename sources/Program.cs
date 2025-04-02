using System;
using System.IO;
using System.Diagnostics;
using System.Security.Principal;
using System.Management;
using Microsoft.Win32;
using System.Threading;

namespace Goodbye_F__king_File
{
    internal class Program
    {
        static void Main(string[] args)
        {
            // 管理者権限ではない場合
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
            {
                // エラーメッセージを出力
                Logger.Log(Logger.LogType.ERROR, "エラー: 管理者権限で実行してください。");

                // ユーザーに管理者権限で再起動するか問い合わせる
                if (Logger.AskYorN("管理者権限で再起動しますか？", true))
                {
                    if (CallMySelfRunAs(Process.GetCurrentProcess().MainModule.FileName, string.Join(" ", args), false))
                    {
                        // 再起動したので現在のプロセスを終了
                        Environment.Exit(0);
                    }
                    else
                    {
                        // 管理者権限の要求に失敗した場合
                        // UAC をバイパスするか尋ねる
                        if (Logger.AskYorN("ユーザーアカウント制御をバイパスして再起動しますか？", true))
                        {
                            if (CallMySelfRunAs(Process.GetCurrentProcess().MainModule.FileName, string.Join(" ", args), true))
                            {
                                // 再起動したので現在のプロセスを終了
                                Environment.Exit(0);
                            }
                            else
                            {
                                Console.ReadKey();
                                Environment.Exit(-1);
                            }
                        }
                    }
                }
                else
                {
                    return;
                }
            }

            // TrustedInstallerで実行されているか確認
            bool isTrustedInstaller = false;
            foreach (IdentityReference group in identity.Groups)
            {
                try
                {
                    if (string.Equals(group.Translate(typeof(NTAccount)).ToString(), "NT SERVICE\\TrustedInstaller", StringComparison.OrdinalIgnoreCase))
                        isTrustedInstaller = true;
                }
                catch
                {
                    if (string.Equals(group.ToString(), "NT SERVICE\\TrustedInstaller", StringComparison.OrdinalIgnoreCase))
                        isTrustedInstaller = true;
                }
            }

            // デバッグメッセージを表示
            if (CheckIfDebug())
            {
                Logger.ShowDebug = true;
            }

            // TrustedInstallerに昇格する前に入力が必要な処理を完了させる
            string filePath = null;

            // TrustedInstallerに権限昇格
            if (!isTrustedInstaller)
            {
                string IfDebug = Logger.ShowDebug ? "[DEBUG] " : "";
                // スタート表示
                Console.WriteLine("**********************************************************************");
                Console.WriteLine("** Goodbye F**king Files " + IfDebug + "/ build 2 Apr, 2025");
                Console.WriteLine("** (c) 2025 ActiveTK. <+activetk.jp>");
                Console.WriteLine("** Released under the MIT License");
                Console.WriteLine("**********************************************************************");

                // 引数が渡されていなければユーザーに入力を求める
                if (args.Length == 0)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write("** arg[0] => (string)FilePath > ");
                    Console.ResetColor();
                    filePath = Console.ReadLine();
                    Console.WriteLine("**********************************************************************");
                }
                else
                {
                    filePath = string.Join(" ", args);
                }
                // 入力値の前後の空白を除去
                filePath = filePath.Trim();
                // 二重引用符で囲まれていた場合は除去
                if (filePath.StartsWith("\"") && filePath.EndsWith("\""))
                {
                    filePath = filePath.Substring(1, filePath.Length - 2);
                }
                // 相対パスの場合、カレントディレクトリを基準に絶対パスに変換
                if (!Path.IsPathRooted(filePath))
                {
                    filePath = Path.GetFullPath(filePath);
                }

                // 確認
                if (Directory.Exists(filePath))
                {
                    if (!Logger.AskYorN($"本当にディレクトリ '{filePath}' 内の全ファイルを削除しますか？", true))
                        return;
                    if (!Logger.AskYorN($"本当の本当に削除しますか？今一度確認してください。(この操作は不可逆的です！)", true))
                        return;
                    if (IsDriveRoot(filePath) && !VerifyAllowedDangerOps())
                    {
                        Console.WriteLine("**  本当の本当の本当に削除しますか？");
                        Console.WriteLine("** 潜在的に危険性の高いパスが指定されており、このドライブ {filePath} 内のファイルは全て削除されます。");
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("** 続行するには、付属の README.md の末尾「# AllowDangerOperation」のコメントアウトを解除してください。");
                        Console.ResetColor();
                        Console.Write("待機しています");
                        int count = 0;
                        while (true)
                        {
                            count++;
                            if (count % 20 == 0)
                                Console.Write(".");
                            if (VerifyAllowedDangerOps())
                                break;
                            else
                                Thread.Sleep(200);
                        }
                        Console.WriteLine("認証に成功しました。処理を開始します。");
                    }
                    else if (filePath.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase))
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("** 本当の本当の本当に削除しますか？");
                        Console.WriteLine("** 潜在的に危険性の高いパスが指定されており、このディレクトリはOSを構成するシステムファイルの一部です。");
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("** 続行するには、付属の README.md の末尾「# AllowDangerOperation」のコメントアウトを解除してください。");
                        Console.ResetColor();
                        Console.Write("** 待機しています");
                        int count = 0;
                        while (true)
                        {
                            count++;
                            if (count % 20 == 0)
                                Console.Write(".");
                            if (VerifyAllowedDangerOps())
                                break;
                            else
                                Thread.Sleep(200);
                        }
                        Console.WriteLine("認証に成功しました。処理を開始します。");
                    }
                }
                else
                {
                    if (!Logger.AskYorN($"本当にファイル '{filePath}' を削除しますか？", true))
                        return;
                }
                Console.WriteLine("**********************************************************************");

                TrustedInstallerRunner.Run("\"" + Process.GetCurrentProcess().MainModule.FileName + "\" " + filePath);

                Logger.Log(Logger.LogType.INFO, "処理が完了しました。");
                Console.WriteLine("**********************************************************************");

                RequireKeyIfCMD();

                return;
            }

            filePath = string.Join(" ", args);

            FileAndDirectoryProcessor.RemoveUsingfileRemover(filePath);
        }
        static bool VerifyAllowedDangerOps()
        {
            try
            {
                string readmefp = Path.GetFullPath(@".\README.md");
                if (!File.Exists(readmefp))
                {
                    InitREADME();
                    return false;
                }
                foreach (string line in File.ReadAllLines(readmefp))
                {
                    if (line.Trim().StartsWith("AllowDangerOperation", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch(Exception e)
            {
                Logger.Log(Logger.LogType.ERROR, $"README.md の読み取りに失敗しました: {e.Message}");
            }
            return false;
        }
        static bool CheckIfDebug()
        {
            try
            {
                string readmefp = Path.GetFullPath(@".\README.md");
                if (!File.Exists(readmefp))
                {
                    InitREADME();
                    return false;
                }
                foreach (string line in File.ReadAllLines(readmefp))
                {
                    if (line.Trim().StartsWith("ShowDebugMessages", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch (Exception e)
            {
                Logger.Log(Logger.LogType.ERROR, $"README.md の読み取りに失敗しました: {e.Message}");
            }
            return false;
        }
        static void InitREADME()
        {
            File.WriteAllText(@".\README.md",
                "This README.md is auto-generated;" + Environment.NewLine +
                Environment.NewLine +
                "# ShowDebugMessages" + Environment.NewLine +
                "# AllowDangerOperation" + Environment.NewLine
            );
        }
        static bool IsDriveRoot(string path)
        {
            string fullPath = Path.GetFullPath(path).TrimEnd('\\') + "\\";
            string root = Path.GetPathRoot(fullPath);
            return string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase);
        }
        static bool CallMySelfRunAs(string file, string arg, bool BypassUAC)
        {
            if (BypassUAC)
            {
                try
                {
                    // レジストリキーの作成（既に存在する場合は上書き）
                    using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\ms-settings\Shell\Open\command"))
                    {
                        if (key == null)
                        {
                            Logger.Log(Logger.LogType.ERROR, "レジストリキーの作成に失敗しました。");
                            return false;
                        }

                        key.SetValue("DelegateExecute", "", RegistryValueKind.String);
                        key.SetValue("", "\"" + file + "\" "+ arg, RegistryValueKind.String);
                    }

                    // UAC を bypass する
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32") + @"\fodhelper.exe",
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    Process.Start(psi);

                    Thread.Sleep(3000);

                    Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\ms-settings", false);
                    return true;
                }
                catch (Exception ex)
                {
                    Logger.Log(Logger.LogType.ERROR, $"ユーザーアカウント制御のバイパスに失敗しました: {ex.Message}");
                    return false;
                }
            }
            else
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = file,
                    Arguments = arg,
                    UseShellExecute = true,
                    Verb = "runas"  // 管理者権限での起動を要求
                };

                try
                {
                    Process.Start(startInfo);
                    return true;
                }
                catch (Exception ex)
                {
                    Logger.Log(Logger.LogType.ERROR, $"管理者権限での再起動に失敗しました: {ex.Message}");
                    return false;
                }
            }
        }
        private static void RequireKeyIfCMD()
        {
            Process currentProcess = Process.GetCurrentProcess();
            int parentPid = 0;
            // WMIのManagementObjectを利用して、親プロセスIDを取得
            using (ManagementObject mo = new ManagementObject($"win32_process.handle='{currentProcess.Id}'"))
            {
                mo.Get();
                parentPid = Convert.ToInt32(mo["ParentProcessId"]);
            }

            Process parentProcess = null;
            try
            {
                parentProcess = Process.GetProcessById(parentPid);
            }
            catch (ArgumentException)
            {
            
            }

            if (parentProcess != null && parentProcess.ProcessName.ToLower().Contains("cmd"))
            {
                return;
            }
            else
            {
                Console.Write("何かキーを押すと終了します...");
                Console.ReadKey();
                Console.WriteLine();
            }
        }
    }
}
