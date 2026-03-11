using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Antigravity02.Agents;

namespace Antigravity02
{
    public delegate void CommandHandler(string args, BaseAgent agent, out bool shouldExit);

    public class CommandDefinition
    {
        public string Name { get; }
        public string Description { get; }
        public CommandHandler Handler { get; }

        public CommandDefinition(string name, string description, CommandHandler handler)
        {
            Name = name;
            Description = description;
            Handler = handler;
        }
    }

    public static class CommandManager
    {
        private static readonly Dictionary<string, CommandDefinition> _commands = new Dictionary<string, CommandDefinition>(StringComparer.OrdinalIgnoreCase);

        static CommandManager()
        {
            RegisterCommand("/exit", "結束程式", (string args, BaseAgent agent, out bool shouldExit) =>
            {
                shouldExit = true;
            });

            RegisterCommand("/help", "顯示此幫助訊息", (string args, BaseAgent agent, out bool shouldExit) =>
            {
                shouldExit = false;
                ShowHelp();
            });

            RegisterCommand("/new", "開始新的對話 (清除對話紀錄)", (string args, BaseAgent agent, out bool shouldExit) =>
            {
                shouldExit = false;
                agent.ClearChatHistory();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("[System] 對話紀錄已清除，開始新的對話。");
                Console.ResetColor();
            });

            RegisterCommand("/save", "儲存對話紀錄至檔案 (預設: chat_history.json)", (string args, BaseAgent agent, out bool shouldExit) =>
            {
                shouldExit = false;
                string path = "chat_history.json";
                if (!string.IsNullOrWhiteSpace(args)) path = args.Trim();
                if (agent.SaveChatHistory(path))
                {
                    Console.WriteLine($"[System] Chat history saved to {path}");
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[Error] Failed to save chat history to {path}");
                    Console.ResetColor();
                }
            });

            RegisterCommand("/load", "從檔案載入對話紀錄 (預設: chat_history.json)", (string args, BaseAgent agent, out bool shouldExit) =>
            {
                shouldExit = false;
                string path = "chat_history.json";
                if (!string.IsNullOrWhiteSpace(args)) path = args.Trim();
                if (agent.LoadChatHistory(path))
                {
                    Console.WriteLine($"[System] Chat history loaded from {path}");
                    DisplayChatHistory(agent.GetChatHistory(), agent.SmartClient);
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[Error] Failed to load chat history from {path}");
                    Console.ResetColor();
                }
            });

            RegisterCommand("/time", "切換使用者訊息的時間戳記顯示", (string args, BaseAgent agent, out bool shouldExit) =>
            {
                shouldExit = false;
                agent.EnableTimestampHeader = !agent.EnableTimestampHeader;
                string status = agent.EnableTimestampHeader ? "ON" : "OFF";
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"[System] Timestamp header is now {status}.");
                Console.ResetColor();
            });

            RegisterCommand("/rmock", "切換是否紀錄 AI API 回應至 MockData 資料夾", (string args, BaseAgent agent, out bool shouldExit) =>
            {
                shouldExit = false;
                Antigravity02.Tools.MockDataManager.IsRecordingMockData = !Antigravity02.Tools.MockDataManager.IsRecordingMockData;
                string status = Antigravity02.Tools.MockDataManager.IsRecordingMockData ? "ON" : "OFF";
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"[System] Record mock data is now {status}.");
                Console.ResetColor();
            });
        }

        public static void RegisterCommand(string name, string description, CommandHandler handler)
        {
            _commands[name] = new CommandDefinition(name, description, handler);
        }

        public static string[] GetRegisteredCommandNames()
        {
            return _commands.Keys.ToArray();
        }

        public static bool TryHandleCommand(string input, BaseAgent agent, out bool shouldExit)
        {
            shouldExit = false;
            if (string.IsNullOrEmpty(input)) return false;

            // 只處理以 / 開頭的指令
            if (!input.StartsWith("/")) return false;

            string cmdLine = input.Trim();
            string[] parts = cmdLine.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            string cmdName = parts[0];
            string cmdArgs = parts.Length > 1 ? parts[1] : string.Empty;

            if (_commands.TryGetValue(cmdName, out var def))
            {
                try
                {
                    def.Handler(cmdArgs, agent, out shouldExit);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[System] 指令執行時發生錯誤：{ex.Message}");
                }
            }
            else
            {
                Console.WriteLine($"[System] Unknown command: {cmdName}. Type /help for list of commands.");
            }
            return true;
        }

        public static void ShowHelp()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n=== Available Commands ===");
            foreach (var kvp in _commands)
            {
                Console.WriteLine($"  {kvp.Value.Name,-14}: {kvp.Value.Description}");
            }
            Console.WriteLine("==========================\n");
            Console.ResetColor();
        }

        /// <summary>
        /// 將對話紀錄以摘要方式顯示在畫面上，讓使用者了解對話脈絡
        /// </summary>
        private static void DisplayChatHistory(ReadOnlyCollection<object> chatHistory, Antigravity02.AIClient.IAIClient client)
        {
            if (chatHistory == null || chatHistory.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine("[System] 對話紀錄為空。");
                Console.ResetColor();
                return;
            }

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\n[System] === 載入的對話紀錄 ===");
            Console.ResetColor();

            foreach (var entry in chatHistory)
            {
                if (!client.TryGetRoleAndPartsFromMessage(entry, out string role, out var parts)) continue;

                if (role == "user")
                {
                    foreach (object part in parts)
                    {
                        if (client.TryGetTextFromPart(part, out string text))
                        {
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"\nUser: {text}");
                            Console.ResetColor();
                        }
                    }
                }
                else if (role == "model")
                {
                    foreach (object part in parts)
                    {
                        if (client.TryGetTextFromPart(part, out string text))
                        {
                            Console.ForegroundColor = ConsoleColor.White;
                            Console.WriteLine($"\nAI: {text}");
                            Console.ResetColor();
                        }
                        if (client.TryGetFunctionCallFromPart(part, out string funcName, out var funcArgs))
                        {
                            Console.ForegroundColor = ConsoleColor.Blue;
                            Console.WriteLine($"  [Tool Call] {funcName ?? "?"}");
                            Console.ResetColor();
                        }
                    }
                }
                else if (role == "function")
                {
                    foreach (object part in parts)
                    {
                        if (client.TryGetFunctionResponseFromPart(part, out string funcName, out string content))
                        {
                            content = content ?? "";
                            string summary = content.Length > 80 ? content.Substring(0, 80) + "..." : content;
                            Console.ForegroundColor = ConsoleColor.Gray;
                            Console.WriteLine($"  [Tool Result] {funcName ?? "?"}: {summary}");
                            Console.ResetColor();
                        }
                    }
                }
            }

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("[System] === 對話紀錄結束 ===\n");
            Console.ResetColor();
        }
    }
}
