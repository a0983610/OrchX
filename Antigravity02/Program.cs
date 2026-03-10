using System;
using Antigravity02.Config;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Antigravity02.Agents;
using Antigravity02.Tools;
using Antigravity02.UI;
using Antigravity02.AIClient;

namespace Antigravity02
{
    internal class Program
    {
        private static readonly string EnvPath = Path.Combine(AppContext.BaseDirectory, ".env");
        private static CancellationTokenSource _currentCts;

        static async Task Main(string[] args)
        {
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                if (_currentCts != null && !_currentCts.IsCancellationRequested)
                {
                    _currentCts.Cancel();
                }
            };

            var (apiKey, smartModel, fastModel) = await InitializeConfigurationAsync();

            PrintStartupBanner(apiKey);

            // 無論有沒有 API Key 都會進入執行範圍，遇到需要發 API 時可由底層讀取 MockData
            var smartClient = new GeminiClient(apiKey, smartModel);
            var fastClient = new GeminiClient(apiKey, fastModel);

            var agent = new UniversalAgent(
                smartClient,
                fastClient,
                Antigravity02.Config.AgentConfig.GetSystemInstruction()
            );

            PrintModelConfig(smartModel, fastModel);

            var ui = new ConsoleUI();

            bool shouldExitFromArgs = await ProcessStartupArgsAsync(args, agent, ui);
            if (shouldExitFromArgs) return;

            await RunInteractiveLoopAsync(agent, ui);

            Console.WriteLine("\nProgram finished.");
            if (!Console.IsInputRedirected) { Console.ReadKey(); }
        }

        static async Task<(string apiKey, string smartModel, string fastModel)> InitializeConfigurationAsync()
        {
            // 確保環境變量檔案存在
            EnsureEnvFileExists();

            // --- 配置獲取 ---
            string apiKey = GetApiKey();
            string smartModelRaw = GetConfig("GEMINI_SMART_MODEL") ?? GetConfig("GEMINI_MODEL");
            string fastModelRaw = GetConfig("GEMINI_FAST_MODEL") ?? GetConfig("GEMINI_MODEL");
            
            bool noModelConfigured = string.IsNullOrEmpty(smartModelRaw) && string.IsNullOrEmpty(fastModelRaw);

            // 使用者指定預設為 gemini-2.5-flash
            string smartModel = smartModelRaw ?? "gemini-2.5-flash";
            string fastModel = fastModelRaw ?? "gemini-2.5-flash";
            // ----------------

            if (noModelConfigured && !string.IsNullOrEmpty(apiKey))
            {
                // 不顯示在介面，而是默默更新 .env 檔案
                await UpdateEnvWithModelListAsync(apiKey);
            }

            return (apiKey, smartModel, fastModel);
        }

        static void PrintStartupBanner(string apiKey)
        {
            Console.WriteLine("=== AI Automation Assistant ===");

            if (string.IsNullOrEmpty(apiKey))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("\n[Warning] API Key is empty. Application will run in Mock API Mode.");
                Console.ResetColor();
            }
        }

        static void PrintModelConfig(string smartModel, string fastModel)
        {
            // 顯示目前使用的模型
            Console.ForegroundColor = ConsoleColor.Cyan;
            if (smartModel == fastModel)
            {
                Console.WriteLine($"[Config] Model: {smartModel}");
            }
            else
            {
                Console.WriteLine($"[Config] Smart Model: {smartModel}");
                Console.WriteLine($"[Config] Fast Model : {fastModel}");
            }
            Console.ResetColor();
        }

        static async Task RunInteractiveLoopAsync(UniversalAgent agent, ConsoleUI ui)
        {
            while (true)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write($"\nUser: ");
                Console.ResetColor();

                string input = Antigravity02.UI.ConsoleInputHelper.ReadConsoleInput(CommandManager.GetRegisteredCommandNames());
                if (input == null) break;
                if (string.IsNullOrEmpty(input)) continue;

                // --- 指令處理 ---
                if (CommandManager.TryHandleCommand(input, agent, out bool shouldExit))
                {
                    if (shouldExit) break;
                    continue;
                }
                // -----------------------

                try
                {
                    _currentCts = new CancellationTokenSource();
                    await agent.ExecuteAsync(input, ui, _currentCts.Token);
                }
                catch (OperationCanceledException)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("\n[System] 執行已手動中斷。");
                    Console.ResetColor();
                }
                catch (Exception ex)
                {
                    UsageLogger.LogError($"System Error: {ex.ToString()}");
                    ui.ReportError(ex.Message);

                    BackupChatHistoryOnError(agent, ui);
                }
            }
        }

        static async Task<bool> ProcessStartupArgsAsync(string[] args, UniversalAgent agent, ConsoleUI ui)
        {
            if (args.Length == 0) return false;

            string initialInput = string.Join(" ", args);
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"\n[System] Detected arguments: {initialInput}");
            Console.ResetColor();

            bool isCommand = CommandManager.TryHandleCommand(initialInput, agent, out bool startShouldExit);
            if (isCommand)
            {
                if (startShouldExit) return true; // 若指令為 /exit，直接結束程式
            }
            else
            {
                // 若非指令，則視為 Prompt 直接執行
                try
                {
                    _currentCts = new CancellationTokenSource();
                    await agent.ExecuteAsync(initialInput, ui, _currentCts.Token);
                }
                catch (OperationCanceledException)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("\n[System] 啟動參數執行已手動中斷。");
                    Console.ResetColor();
                }
                catch (Exception ex)
                {
                    UsageLogger.LogError($"Startup Args Error: {ex.ToString()}");
                    ui.ReportError(ex.Message);

                    BackupChatHistoryOnError(agent, ui);
                }
            }
            return false;
        }

        static void BackupChatHistoryOnError(UniversalAgent agent, ConsoleUI ui)
        {
            if (agent.SaveChatHistory("system_error_backup.json"))
            {
                ui.ReportError("系統發生非預期錯誤，對話紀錄已備份至 system_error_backup.json");
            }
            else
            {
                ui.ReportError("系統發生非預期錯誤，且無法備份對話紀錄。");
            }
        }

        static async Task UpdateEnvWithModelListAsync(string apiKey)
        {
            if (!File.Exists(EnvPath)) return;

            try
            {
                string envContent = File.ReadAllText(EnvPath);
                // 如果已經有模型列表註解，就不重複查詢寫入
                if (envContent.Contains("# --- 自動查詢可用模型列表 ---")) return;

                var tempClient = new Antigravity02.AIClient.GeminiClient(apiKey);
                string json = await tempClient.ListModelsAsync();

                var data = JsonTools.Deserialize<Dictionary<string, object>>(json);
                
                var modelDocs = new System.Text.StringBuilder();
                modelDocs.AppendLine("\n# --- 自動查詢可用模型列表 ---");

                if (data.ContainsKey("models"))
                {
                    var models = data["models"] as System.Collections.ArrayList;
                    if (models != null)
                    {
                        foreach (Dictionary<string, object> model in models)
                        {
                            string name = model["name"].ToString().Replace("models/", "");
                            string displayName = model["displayName"].ToString();
                            
                            var methods = model["supportedGenerationMethods"] as System.Collections.ArrayList;
                            if (methods != null && methods.Contains("generateContent"))
                            {
                                modelDocs.AppendLine($"# {name,-25} : {displayName}");
                            }
                        }
                    }
                }
                modelDocs.AppendLine("# ------------------------------");

                // 插入到模型設置區塊之前
                if (envContent.Contains("GEMINI_MODEL="))
                {
                    envContent = envContent.Replace("GEMINI_MODEL=", modelDocs.ToString() + "GEMINI_MODEL=");
                }
                else
                {
                    envContent += modelDocs.ToString();
                }

                File.WriteAllText(EnvPath, envContent);
                Console.WriteLine($"[System] 已自動查詢可用模型列表，並寫入 .env 供參考。");
            }
            catch
            {
                // 靜默失敗，不影響主程式運行
            }
        }

        static string GetApiKey() => GetConfig("GEMINI_API_KEY");
        
        static string GetConfig(string keyName)
        {
            // 1. 優先從系統環境變量讀取
            string value = Environment.GetEnvironmentVariable(keyName);
            if (!string.IsNullOrEmpty(value)) return value;

            // 2. 嘗試從本地 .env 檔案讀取
            if (File.Exists(EnvPath))
            {
                var lines = File.ReadAllLines(EnvPath);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("#") || string.IsNullOrEmpty(trimmed)) continue; // 跳過註解與空行
                    string prefix = keyName + "=";
                    if (trimmed.StartsWith(prefix))
                    {
                        string result = trimmed.Substring(prefix.Length).Trim().Trim('\'', '"');
                        return string.IsNullOrEmpty(result) ? null : result; // 空值視為未設定
                    }
                }
            }
            return null;
        }

        static void EnsureEnvFileExists()
        {
            if (!File.Exists(EnvPath))
            {
                string content = "# Gemini API Key (必填)\n" +
                                 "GEMINI_API_KEY=\n\n" +
                                 "# 推理模型設置 (選填，沒填會使用預設值)\n" +
                                 "# 也可以只設 GEMINI_MODEL 讓所有模組共用\n" +
                                 "GEMINI_MODEL=\n" +
                                 "GEMINI_SMART_MODEL=\n" +
                                 "GEMINI_FAST_MODEL=\n";
                
                try
                {
                    File.WriteAllText(EnvPath, content);
                    Console.WriteLine("[System] 已自動創建 .env 配置文件，請填入 API Key 後重新啟動。");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Error] 無法創建 .env 檔案: {ex.Message}");
                }
            }
        }

    }
}
