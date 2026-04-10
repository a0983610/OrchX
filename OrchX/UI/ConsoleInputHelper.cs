using System;
using System.Collections.Generic;
using System.Linq;

namespace OrchX.UI
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
        /// 將輸入字串正規化換行符號為 \n
        /// </summary>
        private static string NormalizeNewlines(string text)
            => text.Replace("\r\n", "\n").Replace("\r", "\n");

        /// <summary>
        /// 提供具備自動完成與指令提示功能的控制台輸入讀取機制。
        /// 支援 Ctrl+Enter 換行輸入，Enter 送出。
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
            try { if (OperatingSystem.IsWindows()) { originalCursorVisible = Console.CursorVisible; } } catch { }

            try
            {
                // 預留給多行輸入 + hint 的緩衝空間
                const int reservedLines = 12;
                int currentTop = Console.CursorTop;
                int maxTopForBuffer = Console.BufferHeight - 1;

                int originalLeft = Console.CursorLeft;
                // 確保底部有足夠空間，避免繪製 Hint 時視窗捲動導致 promptTop 失效
                if (currentTop + reservedLines > maxTopForBuffer)
                {
                    int linesToPush = (currentTop + reservedLines) - maxTopForBuffer;
                    for (int i = 0; i < linesToPush; i++)
                    {
                        Console.WriteLine();
                    }
                    Console.SetCursorPosition(originalLeft, Console.CursorTop - linesToPush);
                }

                int promptLeft = originalLeft;
                int promptTop = Console.CursorTop;

                var input = new System.Text.StringBuilder();
                int cursorIndex = 0;
                int lastHintCount = 0;
                const int maxHintDisplay = 5;

                // 記錄上一次繪製的行數，用來清除多出來的舊行
                int prevLineCount = 1;

                // 記錄每行上次繪製的寬度，用來精確補空白覆蓋刪除的字元
                var prevLineWidths = new Dictionary<int, int>();

                // ── 清除 Hint 列 ──
                void ClearHints(int startRow)
                {
                    for (int i = 0; i < lastHintCount; i++)
                    {
                        int targetRow = startRow + i;
                        if (targetRow >= Console.BufferHeight) break;
                        Console.SetCursorPosition(0, targetRow);
                        Console.Write(new string(' ', Console.WindowWidth - 1));
                    }
                    lastHintCount = 0;
                }

                // ── 繪製輸入區 ──
                void DrawInput(string[] lines)
                {
                    int windowWidth = Console.WindowWidth;

                    // 若行數減少，先清除多餘的舊行
                    for (int i = lines.Length; i < prevLineCount; i++)
                    {
                        int row = promptTop + i;
                        if (row >= Console.BufferHeight) break;
                        Console.SetCursorPosition(0, row);
                        Console.Write(new string(' ', windowWidth - 1));
                        prevLineWidths.Remove(i);
                    }

                    for (int i = 0; i < lines.Length; i++)
                    {
                        int row = promptTop + i;
                        if (row >= Console.BufferHeight) break;

                        int colStart = (i == 0) ? promptLeft : 0;
                        int lineDisplayWidth = GetDisplayWidth(lines[i]);

                        // 計算需要補多少空白才能覆蓋上次比較長的殘字
                        int prevWidth = prevLineWidths.TryGetValue(i, out int pw) ? pw : 0;
                        int padding = Math.Max(0, prevWidth - lineDisplayWidth);
                        // 同時確保不會超出視窗寬度
                        int maxPadding = windowWidth - colStart - lineDisplayWidth - 1;
                        padding = Math.Min(padding, Math.Max(0, maxPadding));

                        Console.SetCursorPosition(colStart, row);
                        Console.Write(lines[i] + new string(' ', padding));

                        prevLineWidths[i] = lineDisplayWidth;
                    }

                    prevLineCount = lines.Length;
                }

                while (true)
                {
                    try { if (OperatingSystem.IsWindows()) { Console.CursorVisible = false; } } catch { }

                    string normalized = NormalizeNewlines(input.ToString());
                    string[] lines = normalized.Split('\n');

                    DrawInput(lines);

                    // ── 指令自動完成建議（只看第一行） ──
                    string firstLine = lines[0];
                    var suggestions = new List<string>();
                    if (firstLine.StartsWith("/") && firstLine.Length >= 2)
                    {
                        string cmdPart = firstLine.Split(' ')[0].ToLower();
                        foreach (var cmd in availableCommands)
                        {
                            if (cmd.StartsWith(cmdPart) && cmd != cmdPart)
                            {
                                suggestions.Add(cmd);
                                if (suggestions.Count >= maxHintDisplay) break;
                            }
                        }
                    }

                    int hintStartRow = promptTop + lines.Length;
                    ClearHints(hintStartRow);

                    lastHintCount = suggestions.Count;
                    if (suggestions.Count > 0)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        for (int i = 0; i < suggestions.Count; i++)
                        {
                            int targetRow = hintStartRow + i;
                            if (targetRow >= Console.BufferHeight) break;
                            Console.SetCursorPosition(0, targetRow);
                            Console.Write("  Hint: " + suggestions[i] + " (按 Tab 自動完成)");
                        }
                        Console.ResetColor();
                    }

                    // ── 計算並設置游標位置 ──
                    string beforeCursor = NormalizeNewlines(input.ToString().Substring(0, cursorIndex));
                    string[] cursorLines = beforeCursor.Split('\n');
                    int cursorRow = promptTop + cursorLines.Length - 1;
                    int colOffset = (cursorLines.Length == 1) ? promptLeft : 0;
                    int cursorCol = colOffset + GetDisplayWidth(cursorLines.Last());

                    if (cursorRow < Console.BufferHeight)
                        Console.SetCursorPosition(Math.Min(cursorCol, Console.WindowWidth - 1), cursorRow);

                    try { if (OperatingSystem.IsWindows()) { Console.CursorVisible = originalCursorVisible; } } catch { }

                    // ── 讀取按鍵 ──
                    var keyInfo = Console.ReadKey(intercept: true);

                    if (keyInfo.Key == ConsoleKey.Enter)
                    {
                        if (keyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
                        {
                            // Ctrl+Enter：插入換行
                            input.Insert(cursorIndex, '\n');
                            cursorIndex++;
                        }
                        else
                        {
                            // Enter：送出，將游標移到最後一行再輸出換行
                            string finalNorm = NormalizeNewlines(input.ToString());
                            int finalLineCount = finalNorm.Split('\n').Length;
                            int finalRow = promptTop + finalLineCount - 1;

                            ClearHints(promptTop + finalLineCount);

                            if (finalRow < Console.BufferHeight)
                                Console.SetCursorPosition(0, finalRow);
                            Console.WriteLine();
                            break;
                        }
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
                        // 移至目前行首
                        string bc = NormalizeNewlines(input.ToString().Substring(0, cursorIndex));
                        int lastNl = bc.LastIndexOf('\n');
                        cursorIndex = (lastNl < 0) ? 0 : lastNl + 1;
                    }
                    else if (keyInfo.Key == ConsoleKey.End)
                    {
                        // 移至目前行尾
                        string norm = NormalizeNewlines(input.ToString());
                        int fromCursor = norm.IndexOf('\n', cursorIndex);
                        cursorIndex = (fromCursor < 0) ? input.Length : fromCursor;
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
                    else if (keyInfo.Key == ConsoleKey.UpArrow)
                    {
                        // 多行時：移至上一行的相同列位；單行時：無動作（TODO: 歷史紀錄）
                        string norm = NormalizeNewlines(input.ToString());
                        string[] allLines = norm.Split('\n');
                        string bc = NormalizeNewlines(input.ToString().Substring(0, cursorIndex));
                        string[] bcLines = bc.Split('\n');
                        int currLineIdx = bcLines.Length - 1;

                        if (currLineIdx > 0)
                        {
                            int currentColWidth = GetDisplayWidth(bcLines.Last());
                            int prevLineIdx = currLineIdx - 1;

                            // 計算上一行起點在 StringBuilder 中的 index
                            int lineStart = 0;
                            for (int i = 0; i < prevLineIdx; i++)
                                lineStart += allLines[i].Length + 1; // +1 for '\n'

                            // 嘗試保持相同的水平顯示位置
                            int targetColWidth = Math.Min(currentColWidth, GetDisplayWidth(allLines[prevLineIdx]));
                            int idx = lineStart;
                            int w = 0;
                            for (int i = 0; i < allLines[prevLineIdx].Length; i++)
                            {
                                int cw = GetDisplayWidth(allLines[prevLineIdx][i].ToString());
                                if (w + cw > targetColWidth) break;
                                w += cw;
                                idx++;
                            }
                            cursorIndex = idx;
                        }
                    }
                    else if (keyInfo.Key == ConsoleKey.DownArrow)
                    {
                        // 多行時：移至下一行的相同列位；單行時：無動作（TODO: 歷史紀錄）
                        string norm = NormalizeNewlines(input.ToString());
                        string[] allLines = norm.Split('\n');
                        string bc = NormalizeNewlines(input.ToString().Substring(0, cursorIndex));
                        string[] bcLines = bc.Split('\n');
                        int currLineIdx = bcLines.Length - 1;

                        if (currLineIdx < allLines.Length - 1)
                        {
                            int currentColWidth = GetDisplayWidth(bcLines.Last());
                            int nextLineIdx = currLineIdx + 1;

                            // 計算下一行起點在 StringBuilder 中的 index
                            int lineStart = 0;
                            for (int i = 0; i <= currLineIdx; i++)
                                lineStart += allLines[i].Length + 1; // +1 for '\n'

                            // 嘗試保持相同的水平顯示位置
                            int targetColWidth = Math.Min(currentColWidth, GetDisplayWidth(allLines[nextLineIdx]));
                            int idx = lineStart;
                            int w = 0;
                            for (int i = 0; i < allLines[nextLineIdx].Length; i++)
                            {
                                int cw = GetDisplayWidth(allLines[nextLineIdx][i].ToString());
                                if (w + cw > targetColWidth) break;
                                w += cw;
                                idx++;
                            }
                            cursorIndex = idx;
                        }
                    }
                    else if (!char.IsControl(keyInfo.KeyChar))
                    {
                        input.Insert(cursorIndex, keyInfo.KeyChar);
                        cursorIndex++;
                    }
                }

                // 回傳時只 TrimEnd 換行，保留使用者有意輸入的內容結構
                return NormalizeNewlines(input.ToString()).TrimEnd('\n').Trim();
            }
            catch
            {
                // Fallback in case of unexpected console error
                return Console.ReadLine();
            }
            finally
            {
                try { if (OperatingSystem.IsWindows()) { Console.CursorVisible = originalCursorVisible; } } catch { }
            }
        }
    }
}
