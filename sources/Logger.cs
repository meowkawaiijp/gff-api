using System;

namespace Goodbye_F__king_File
{
    // ログ表示専用クラス
    public static class Logger
    {
        public enum LogType { INFO, WARN, ERROR, DEBUG }
        public static bool ShowDebug = false;
        public static void Log(LogType type, string msg)
        {
            if (!ShowDebug && type == LogType.DEBUG)
                return;
            var oldColor = Console.ForegroundColor;
            Console.ForegroundColor = GetColorForLogType(type);
            string logMessage = $"** [{type}] {DateTime.Now.ToUniversalTime().ToString("R")} : {msg}";
            Console.WriteLine(logMessage);
            Console.ForegroundColor = oldColor;
        }
        public static void LogNotNewLine(LogType type, string msg)
        {
            Console.ForegroundColor = GetColorForLogType(type);
            string logMessage = $"** [{type}] {DateTime.Now.ToUniversalTime().ToString("R")} : {msg}";
            Console.Write(logMessage);
        }
        public static void LogNotNewLine_Next(string msgRaw)
        {
            Console.WriteLine(msgRaw);
        }

        private static ConsoleColor GetColorForLogType(LogType type)
        {
            switch (type)
            {
                case LogType.INFO: return ConsoleColor.Gray;
                case LogType.WARN: return ConsoleColor.Yellow;
                case LogType.ERROR: return ConsoleColor.Red;
                case LogType.DEBUG: return ConsoleColor.Cyan;
                default: return ConsoleColor.White;
            }
        }
        public static bool AskYorN(string prompt, bool Recommended)
        {
            while (true)
            {
                Console.CursorLeft = 0;
                if (Recommended)
                    Console.Write("*! " + prompt + " (Y/n)> ");
                else
                    Console.Write("*! " + prompt + " (y/N)> ");
                Console.ForegroundColor = ConsoleColor.Green;
                char key = Console.ReadKey().KeyChar;
                Console.ResetColor();
                if (key.ToString().ToLower() == "y")
                {
                    Console.WriteLine();
                    return true;
                }
                else if (key.ToString().ToLower() == "n")
                {
                    Console.WriteLine();
                    return false;
                }
            }
        }
    }
}