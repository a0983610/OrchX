using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Antigravity02.Agents;

namespace Antigravity02
{
    public static class CommandManager
    {
        public static bool TryHandleCommand(string input, BaseAgent agent, out bool shouldExit)
        {
            shouldExit = false;
            if (string.IsNullOrEmpty(input)) return false;

            // 只處理以 / 開頭的指令
            if (!input.StartsWith("/")) return false;

            string cmd = input.Trim();

            if (cmd.Equals("/exit", StringComparison.OrdinalIgnoreCase))
            {
                shouldExit = true;
                return true;
            }

            if (cmd.Equals("/help", StringComparison.OrdinalIgnoreCase))
            {
                ShowHelp();
                return true;
            }

            if (cmd.Equals("/new", StringComparison.OrdinalIgnoreCase))
            {
                agent.ClearChatHistory();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("[System] 對話紀錄已清除，開始新的對話。");
                Console.ResetColor();
                return true;
            }

            if (cmd.Equals("/save", StringComparison.OrdinalIgnoreCase) || cmd.StartsWith("/save ", StringComparison.OrdinalIgnoreCase))
            {
                string path = "chat_history.json";
                if (cmd.Length > 6)
                {
                    string arg = cmd.Substring(6).Trim();
                    if (!string.IsNullOrEmpty(arg)) path = arg;
                }
                if (agent.SaveChatHistory(path))
                {
                    Console.WriteLine($"[System] Chat history saved to {path}");
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[System] Failed to save chat history to {path}");
                    Console.ResetColor();
                }
                return true;
            }

            if (cmd.Equals("/load", StringComparison.OrdinalIgnoreCase) || cmd.StartsWith("/load ", StringComparison.OrdinalIgnoreCase))
            {
                string path = "chat_history.json";
                if (cmd.Length > 6)
                {
                    string arg = cmd.Substring(6).Trim();
                    if (!string.IsNullOrEmpty(arg)) path = arg;
                }
                if (agent.LoadChatHistory(path))
                {
                    Console.WriteLine($"[System] Chat history loaded from {path}");
                    DisplayChatHistory(agent.GetChatHistory(), agent.SmartClient);
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[System] Failed to load chat history from {path}");
                    Console.ResetColor();
                }
                return true;
            }

            if (cmd.Equals("/time", StringComparison.OrdinalIgnoreCase))
            {
                agent.EnableTimestampHeader = !agent.EnableTimestampHeader;
                string status = agent.EnableTimestampHeader ? "ON" : "OFF";
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"[System] Timestamp header is now {status}.");
                Console.ResetColor();
                return true;
            }

            if (cmd.Equals("/rmock", StringComparison.OrdinalIgnoreCase))
            {
                Antigravity02.Tools.MockDataManager.IsRecordingMockData = !Antigravity02.Tools.MockDataManager.IsRecordingMockData;
                string status = Antigravity02.Tools.MockDataManager.IsRecordingMockData ? "ON" : "OFF";
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"[System] Record mock data is now {status}.");
                Console.ResetColor();
                return true;
            }

            // 如果是 / 開頭但未知的指令，提示使用者
            Console.WriteLine($"[System] Unknown command: {cmd}. Type /help for list of commands.");
            return true;
        }

        public static void ShowHelp()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n=== Available Commands ===");
            Console.WriteLine("  /new          : Start a new conversation (clear chat history)");
            Console.WriteLine("  /save [path]  : Save chat history to file (default: chat_history.json)");
            Console.WriteLine("  /load [path]  : Load chat history from file (default: chat_history.json)");
            Console.WriteLine("  /time         : Toggle timestamp header for user messages");
            Console.WriteLine("  /rmock        : Toggle recording of AI API responses to MockData folder");
            Console.WriteLine("  /help         : Show this help message");
            Console.WriteLine("  /exit         : Exit the program");
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
            Console.WriteLine("\n=== 載入的對話紀錄 ===");
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
                        if (client.TryGetFunctionCallFromPart(part, out string funcName, out var args))
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
            Console.WriteLine("=== 對話紀錄結束 ===\n");
            Console.ResetColor();
        }

        /// <summary>
        /// 提供具備自動完成與指令提示功能的控制台輸入讀取機制
        /// </summary>
        public static string ReadConsoleInput()
        {
            bool canUseInteractiveMenu = true;
            try 
            { 
                if (Console.IsOutputRedirected || Console.IsInputRedirected)
                    canUseInteractiveMenu = false;
                _ = Console.CursorTop; // Test if we can read CursorTop
            } 
            catch { canUseInteractiveMenu = false; }

            if (!canUseInteractiveMenu)
            {
                return Console.ReadLine();
            }

            try
            {
                int requiredSpace = 8;
                int currentTop = Console.CursorTop;
                int maxTopForBuffer = Console.BufferHeight - 1;
                
                // 確保底部有足夠空間，避免繪製 Hint 時視窗捲動導致 promptTop 失效
                if (currentTop + requiredSpace > maxTopForBuffer)
                {
                    int linesToPush = (currentTop + requiredSpace) - maxTopForBuffer;
                    for (int i = 0; i < linesToPush; i++)
                    {
                        Console.WriteLine();
                    }
                    Console.SetCursorPosition(Console.CursorLeft, Console.CursorTop - linesToPush);
                }

                int promptLeft = Console.CursorLeft;
                int promptTop = Console.CursorTop;

                var input = new System.Text.StringBuilder();
                int cursorIndex = 0;
                string[] allCommands = { "/new", "/save", "/load", "/time", "/rmock", "/help", "/exit" };
                int lastHintCount = 0;
                const int maxHintDisplay = 5;
                int maxInputLength = Console.WindowWidth - promptLeft - 2; // 防止超出一行寬度

                void ClearHints()
                {
                    for (int i = 0; i < lastHintCount; i++)
                    {
                        int targetRow = promptTop + 1 + i;
                        if (targetRow >= Console.BufferHeight) break;
                        Console.SetCursorPosition(0, targetRow);
                        Console.Write(new string(' ', Console.WindowWidth - 1));
                    }
                    lastHintCount = 0;
                }

                while (true)
                {
                    // Draw input line
                    Console.SetCursorPosition(promptLeft, promptTop);
                    Console.Write(input.ToString() + " "); 
                    Console.SetCursorPosition(promptLeft + cursorIndex, promptTop);

                    string currentInput = input.ToString();
                    var suggestions = new System.Collections.Generic.List<string>();
                    
                    // 只有輸入至少 2 個字元（如 /s）才開始提示，避免輸入 / 時顯示全部指令
                    if (currentInput.StartsWith("/") && currentInput.Length >= 2)
                    {
                        string cmdPart = currentInput.Split(' ')[0].ToLower();
                        foreach (var cmd in allCommands)
                        {
                            if (cmd.StartsWith(cmdPart) && cmd != cmdPart)
                            {
                                suggestions.Add(cmd);
                                if (suggestions.Count >= maxHintDisplay) break;
                            }
                        }
                    }

                    ClearHints();
                    
                    lastHintCount = suggestions.Count;
                    if (suggestions.Count > 0)
                    {
                        Console.ForegroundColor = ConsoleColor.Gray;
                        for (int i = 0; i < suggestions.Count; i++)
                        {
                            int targetRow = promptTop + 1 + i;
                            if (targetRow >= Console.BufferHeight) break;
                            Console.SetCursorPosition(0, targetRow);
                            Console.Write("  Hint: " + suggestions[i] + " (按 Tab 自動完成)");
                        }
                        Console.ResetColor();
                    }

                    Console.SetCursorPosition(promptLeft + cursorIndex, promptTop);

                    var keyInfo = Console.ReadKey(intercept: true);
                    if (keyInfo.Key == ConsoleKey.Enter)
                    {
                        ClearHints();
                        Console.WriteLine();
                        break;
                    }
                    else if (keyInfo.Key == ConsoleKey.Backspace)
                    {
                        if (cursorIndex > 0)
                        {
                            input.Remove(cursorIndex - 1, 1);
                            cursorIndex--;
                            Console.SetCursorPosition(promptLeft + input.Length, promptTop);
                            Console.Write(" ");
                        }
                    }
                    else if (keyInfo.Key == ConsoleKey.Delete)
                    {
                        if (cursorIndex < input.Length)
                        {
                            input.Remove(cursorIndex, 1);
                            Console.SetCursorPosition(promptLeft + input.Length, promptTop);
                            Console.Write(" ");
                        }
                    }
                    else if (keyInfo.Key == ConsoleKey.LeftArrow)
                    {
                        if (cursorIndex > 0) cursorIndex--;
                    }
                    else if (keyInfo.Key == ConsoleKey.RightArrow)
                    {
                        if (cursorIndex < input.Length) cursorIndex++;
                    }
                    else if (keyInfo.Key == ConsoleKey.Home)
                    {
                        cursorIndex = 0;
                    }
                    else if (keyInfo.Key == ConsoleKey.End)
                    {
                        cursorIndex = input.Length;
                    }
                    else if (keyInfo.Key == ConsoleKey.Tab)
                    {
                        if (suggestions.Count > 0)
                        {
                            string completion = suggestions[0];
                            input.Clear();
                            input.Append(completion);
                            cursorIndex = input.Length;
                            
                            Console.SetCursorPosition(promptLeft, promptTop);
                            Console.Write(new string(' ', Console.WindowWidth - promptLeft - 1));
                        }
                    }
                    else if (keyInfo.Key == ConsoleKey.Escape)
                    {
                        // Escape 清除目前的輸入
                        Console.SetCursorPosition(promptLeft, promptTop);
                        Console.Write(new string(' ', input.Length + 1));
                        input.Clear();
                        cursorIndex = 0;
                    }
                    else if (!char.IsControl(keyInfo.KeyChar))
                    {
                        if (input.Length < maxInputLength)
                        {
                            input.Insert(cursorIndex, keyInfo.KeyChar);
                            cursorIndex++;
                        }
                    }
                }

                return input.ToString().Trim();
            }
            catch
            {
                // Fallback in case of unexpected console error
                return Console.ReadLine();
            }
        }
    }
}
