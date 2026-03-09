using System;
using System.Threading.Tasks;

namespace Antigravity02.UI
{
    public class ConsoleUI : IAgentUI
    {
        public void ReportThinking(int iteration, string modelName)
        {
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine($"\n[Thinking Iteration {iteration} ({modelName})] ...");
            Console.ResetColor();
        }

        public void ReportToolCall(string toolName, string args)
        {
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine($"Action: {toolName}");
            Console.ResetColor();
        }

        public void ReportToolResult(string resultSummary)
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            string text = resultSummary ?? "(no result)";
            string summary = text.Length > 100 ? text.Substring(0, 100) + "..." : text;
            Console.WriteLine($"Result: {summary}");
            Console.ResetColor();
        }

        public void ReportTextResponse(string text, string modelName)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"\nAI ({modelName}): {text}");
            Console.ResetColor();
        }

        public void ReportError(string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\nError: {message}");
            Console.ResetColor();
        }

        public void ReportInfo(string message)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(message);
            Console.ResetColor();
        }

        public Task<bool> PromptContinueAsync(string message)
        {
            int selection = PromptSelectionAsync(message, "Yes", "No").GetAwaiter().GetResult();
            return Task.FromResult(selection == 0);
        }

        public Task<int> PromptSelectionAsync(string message, params string[] options)
        {
            if (options == null || options.Length == 0)
            {
                return Task.FromResult(-1);
            }

            bool canUseInteractiveMenu = true;
            try 
            { 
                if (Console.IsOutputRedirected || Console.IsInputRedirected)
                    canUseInteractiveMenu = false;
            } 
            catch { canUseInteractiveMenu = false; }

            if (!canUseInteractiveMenu)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"\n[PROMPT] {message}");
                Console.ResetColor();
                for (int i = 0; i < options.Length; i++)
                {
                    Console.WriteLine($" {i + 1}. {options[i]}");
                }
                Console.Write("請輸入選項數字 (Enter 預設 1): ");
                string input = Console.ReadLine();
                if (int.TryParse(input, out int choice) && choice >= 1 && choice <= options.Length)
                {
                    return Task.FromResult(choice - 1);
                }
                return Task.FromResult(0);
            }

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\n[PROMPT] {message}");
            Console.ResetColor();

            int selectedIndex = 0;
            int startTop = Console.CursorTop;
            bool cursorVisible = true;
            
            try { cursorVisible = Console.CursorVisible; Console.CursorVisible = false; } catch { }

            int windowWidth = 80;
            try { windowWidth = Console.WindowWidth - 1; if (windowWidth < 1) windowWidth = 80; } catch { }

            try
            {
                while (true)
                {
                    try { Console.SetCursorPosition(0, startTop); } catch { }
                    for (int i = 0; i < options.Length; i++)
                    {
                        string prefix = (i == selectedIndex) ? " > " : "   ";
                        string line = $"{prefix}{i + 1}. {options[i]}";
                        if (line.Length < windowWidth) line = line.PadRight(windowWidth);

                        if (i == selectedIndex)
                        {
                            Console.ForegroundColor = ConsoleColor.Black;
                            Console.BackgroundColor = ConsoleColor.White;
                            Console.WriteLine(line);
                            Console.ResetColor();
                        }
                        else
                        {
                            Console.WriteLine(line);
                        }
                    }

                    var keyInfo = Console.ReadKey(true);
                    var key = keyInfo.Key;

                    if (key == ConsoleKey.UpArrow)
                    {
                        selectedIndex--;
                        if (selectedIndex < 0) selectedIndex = options.Length - 1;
                    }
                    else if (key == ConsoleKey.DownArrow)
                    {
                        selectedIndex++;
                        if (selectedIndex >= options.Length) selectedIndex = 0;
                    }
                    else if (key >= ConsoleKey.D1 && key < ConsoleKey.D1 + options.Length)
                    {
                        selectedIndex = key - ConsoleKey.D1;
                        break;
                    }
                    else if (key >= ConsoleKey.NumPad1 && key < ConsoleKey.NumPad1 + options.Length)
                    {
                        selectedIndex = key - ConsoleKey.NumPad1;
                        break;
                    }
                    else if (key == ConsoleKey.Enter)
                    {
                        break;
                    }
                }

                try { Console.SetCursorPosition(0, startTop); } catch { }
                for (int i = 0; i < options.Length; i++)
                {
                    Console.WriteLine(new string(' ', windowWidth));
                }
                try { Console.SetCursorPosition(0, startTop); } catch { }
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"已選擇: {options[selectedIndex]}");
                Console.ResetColor();

                return Task.FromResult(selectedIndex);
            }
            finally
            {
                try { Console.CursorVisible = cursorVisible; } catch { }
            }
        }
    }
}
