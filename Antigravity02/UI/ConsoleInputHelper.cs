using System;
using System.Collections.Generic;
using System.Linq;

namespace Antigravity02.UI
{
    public static class ConsoleInputHelper
    {
        /// <summary>
        /// 計算字串的顯示寬度（全形字元算 2 寬度）
        /// </summary>
        private static int GetDisplayWidth(string text, int endIndex = -1)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            if (endIndex < 0 || endIndex > text.Length) endIndex = text.Length;
            int width = 0;
            for (int i = 0; i < endIndex; i++)
            {
                char c = text[i];
                if ((c >= 0x4E00 && c <= 0x9FFF) ||
                    (c >= 0x3400 && c <= 0x4DBF) ||
                    (c >= 0x3000 && c <= 0x303F) ||
                    (c >= 0xFF00 && c <= 0xFFEF))
                {
                    width += 2;
                }
                else
                {
                    width += 1;
                }
            }
            return width;
        }

        /// <summary>
        /// 提供具備自動完成與指令提示功能的控制台輸入讀取機制
        /// </summary>
        public static string ReadConsoleInput(string[] availableCommands)
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

            bool originalCursorVisible = true;
            try { originalCursorVisible = Console.CursorVisible; } catch { }

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
                int lastHintCount = 0;
                const int maxHintDisplay = 5;
                int maxDisplayWidth = Console.WindowWidth - promptLeft - 2; // 防止超出一行寬度
                int previousInputWidth = 0;

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
                    try { Console.CursorVisible = false; } catch { }

                    string currentInputStr = input.ToString();
                    int currentStrWidth = GetDisplayWidth(currentInputStr);
                    int padSpaces = Math.Max(2, previousInputWidth - currentStrWidth + 2);

                    // Draw input line
                    Console.SetCursorPosition(promptLeft, promptTop);
                    Console.Write(currentInputStr + new string(' ', padSpaces)); 
                    previousInputWidth = currentStrWidth;

                    string currentInput = currentInputStr;
                    var suggestions = new List<string>();
                    
                    // 只有輸入至少 2 個字元（如 /s）才開始提示，避免輸入 / 時顯示全部指令
                    if (currentInput.StartsWith("/") && currentInput.Length >= 2)
                    {
                        string cmdPart = currentInput.Split(' ')[0].ToLower();
                        foreach (var cmd in availableCommands)
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
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        for (int i = 0; i < suggestions.Count; i++)
                        {
                            int targetRow = promptTop + 1 + i;
                            if (targetRow >= Console.BufferHeight) break;
                            Console.SetCursorPosition(0, targetRow);
                            Console.Write("  Hint: " + suggestions[i] + " (按 Tab 自動完成)");
                        }
                        Console.ResetColor();
                    }

                    Console.SetCursorPosition(promptLeft + GetDisplayWidth(currentInputStr, cursorIndex), promptTop);

                    try { Console.CursorVisible = originalCursorVisible; } catch { }

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
                        }
                    }
                    else if (keyInfo.Key == ConsoleKey.Delete)
                    {
                        if (cursorIndex < input.Length)
                        {
                            input.Remove(cursorIndex, 1);
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
                        }
                    }
                    else if (keyInfo.Key == ConsoleKey.Escape)
                    {
                        // Escape 清除目前的輸入
                        input.Clear();
                        cursorIndex = 0;
                    }
                    else if (!char.IsControl(keyInfo.KeyChar))
                    {
                        if (currentStrWidth + GetDisplayWidth(keyInfo.KeyChar.ToString()) <= maxDisplayWidth)
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
            finally
            {
                try { Console.CursorVisible = originalCursorVisible; } catch { }
            }
        }
    }
}
