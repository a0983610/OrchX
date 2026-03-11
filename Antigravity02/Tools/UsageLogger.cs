using System;
using System.IO;

namespace Antigravity02.Tools
{
    public static class UsageLogger
    {
        private static readonly object _logLock = new object();
        private static readonly string LogFolderName = "logs";
        private static int _sessionCallCount = 0;

        private static string GetLogFilePath()
        {
            string baseDir = AppContext.BaseDirectory;
            string logPath = Path.Combine(baseDir, LogFolderName);

            if (!Directory.Exists(logPath))
            {
                Directory.CreateDirectory(logPath);
            }

            string fileName = DateTime.Now.ToString("yyyy-MM-dd") + ".txt";
            return Path.Combine(logPath, fileName);
        }

        public static void LogApiUsage(string modelName, long durationMs, int promptTokens, int candidateTokens, int totalTokens)
        {
            try
            {
                _sessionCallCount++;
                string logEntry = $"[{DateTime.Now:HH:mm:ss}] " +
                                  $"Call #{_sessionCallCount} | " +
                                  $"Model: {modelName} | " +
                                  $"Duration: {durationMs}ms | " +
                                  $"Tokens: [Prompt: {promptTokens}, Candidate: {candidateTokens}, Total: {totalTokens}]" +
                                  Environment.NewLine;
                lock (_logLock)
                {
                    File.AppendAllText(GetLogFilePath(), logEntry);
                }            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Logger Error] {ex.Message}");
            }
        }

        public static void LogAction(string actionName, string resultSummary)
        {
            try
            {
                string text = resultSummary ?? "(null)";
                string summary = text.Length > 200 ? text.Substring(0, 200) + "..." : text;
                string logEntry = $"[{DateTime.Now:HH:mm:ss}] [ACTION] {actionName} | Result: {summary}{Environment.NewLine}";
                lock (_logLock)
                {
                    File.AppendAllText(GetLogFilePath(), logEntry);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Logger Error] {ex.Message}");
            }
        }

        public static void LogError(string message)
        {
            try
            {
                string logEntry = $"[{DateTime.Now:HH:mm:ss}] [ERROR] {message}{Environment.NewLine}";
                lock (_logLock)
                {
                    File.AppendAllText(GetLogFilePath(), logEntry);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Logger Error] {ex.Message}");
            }
        }

        public static void LogApiError(string message, string requestBody, string responseBody)
        {
            try
            {
                string baseDir = AppContext.BaseDirectory;
                string errFolderPath = Path.Combine(baseDir, "err");

                if (!Directory.Exists(errFolderPath))
                {
                    Directory.CreateDirectory(errFolderPath);
                }

                // 生成唯一編號: ERR_yyyyMMdd_HHmmss_Guid前8碼
                string errorId = $"ERR_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
                string errFileName = $"{errorId}.txt";
                string errFilePath = Path.Combine(errFolderPath, errFileName);

                string detailContent = $"Message: {message}{Environment.NewLine}{Environment.NewLine}" +
                                       $"--- API REQUEST ---{Environment.NewLine}{requestBody}{Environment.NewLine}{Environment.NewLine}" +
                                       $"--- API RESPONSE ---{Environment.NewLine}{responseBody}";

                lock (_logLock)
                {
                    File.WriteAllText(errFilePath, detailContent);

                    // 在主 log 紀錄簡短訊息並附上編號
                    string logEntry = $"[{DateTime.Now:HH:mm:ss}] [ERROR] {message} | Details in err/{errFileName}{Environment.NewLine}";
                    File.AppendAllText(GetLogFilePath(), logEntry);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Logger Error] {ex.Message}");
            }
        }
    }
}
