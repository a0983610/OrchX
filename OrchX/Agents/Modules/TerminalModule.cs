using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OrchX.AIClient;
using OrchX.UI;

namespace OrchX.Agents
{
    public class TerminalModule : BaseAgentModule
    {
        protected override IEnumerable<object> BuildToolDeclarations(IAIClient client)
        {
            yield return client.CreateFunctionDeclaration(
                "run_terminal_command",
                "【系統：執行終端指令】僅允許白名單指令(python, pip, node, npm, npx, dir, echo, type, move, del, ren, mkdir, rmdir, git status/log)。禁用重新導向(><)、管線(|)、連鎖(&)、變數(%)及路徑穿越(..)。限制：npm install 須帶 --ignore-scripts；禁 npm run/pip install/python -m pip install；git log 禁 -p/--all；dir/type 限當前目錄。",
                new
                {
                    type = "object",
                    properties = new
                    {
                        commands = new
                        {
                            type = "array",
                            description = "要依序執行的 CMD 終端指令清單",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    command = new { type = "string", description = "要執行的 CMD 終端指令" },
                                    reason = new { type = "string", description = "為什麼需要執行這項指令？" },
                                    explanation = new { type = "string", description = "這項指令具體會做什麼？（詳細解釋指令參數與作用）" }
                                },
                                required = new[] { "command", "reason", "explanation" }
                            }
                        }
                    },
                    required = new[] { "commands" }
                }
            );
        }

        public override async Task<string> TryHandleToolCallAsync(string funcName, Dictionary<string, object> args, IAgentUI ui, CancellationToken cancellationToken = default)
        {
            if (funcName == "run_terminal_command")
            {
                return await HandleRunTerminalCommandAsync(funcName, args, ui, cancellationToken);
            }
            return null;
        }

        private async Task<string> HandleRunTerminalCommandAsync(string funcName, Dictionary<string, object> args, IAgentUI ui, CancellationToken cancellationToken)
        {
            string err = CheckRequiredArgs(funcName, args);
            if (err != null) return err;

            if (!args.ContainsKey("commands") || !(args["commands"] is System.Collections.ArrayList commandsList))
            {
                return "Error: 'commands' 必須是一個陣列。";
            }

            var commandsToExecute = new List<(string cmd, string reason, string explanation)>();
            System.Text.StringBuilder promptBuilder = new System.Text.StringBuilder();
            promptBuilder.AppendLine("\n[TerminalModule] AI 請求依序執行以下終端指令:");
            promptBuilder.AppendLine("========================================");

            for (int i = 0; i < commandsList.Count; i++)
            {
                if (commandsList[i] is Dictionary<string, object> cmdObj)
                {
                    string command = cmdObj.ContainsKey("command") ? cmdObj["command"]?.ToString() : "";
                    string reason = cmdObj.ContainsKey("reason") ? cmdObj["reason"]?.ToString() : "";
                    string explanation = cmdObj.ContainsKey("explanation") ? cmdObj["explanation"]?.ToString() : "";

                    string restrictedReason = CheckRestrictedCommand(command);
                    if (restrictedReason != null)
                    {
                        return $"Error: 檢測到受限制的破壞性指令 [{command}]。原因: {restrictedReason}";
                    }

                    commandsToExecute.Add((command, reason, explanation));

                    promptBuilder.AppendLine($"【指令 {i + 1}】");
                    promptBuilder.AppendLine($"▶ 執行指令: {command}");
                    promptBuilder.AppendLine($"▶ 執行原因: {reason}");
                    promptBuilder.AppendLine($"▶ 指令詳解: {explanation}");
                    if (i < commandsList.Count - 1)
                    {
                        promptBuilder.AppendLine("----------------------------------------");
                    }
                }
            }
            promptBuilder.AppendLine("========================================\n");
            promptBuilder.Append("是否允許執行這些外部指令? (Y(是) / N(否 / 其他輸入皆視為否)): ");

            // Ask user for permission once for all commands
            bool allowed = await ui.PromptContinueAsync(promptBuilder.ToString());

            if (!allowed)
            {
                ui.ReportInfo($"[TerminalModule] 使用者拒絕執行上述 {commandsToExecute.Count} 個指令。");
                return $"[已拒絕] 使用者不同意執行指令。";
            }

            ui.ReportInfo($"[TerminalModule] 使用者已同意，準備依序執行 {commandsToExecute.Count} 個指令...");
            
            System.Text.StringBuilder resultBuilder = new System.Text.StringBuilder();

            for (int i = 0; i < commandsToExecute.Count; i++)
            {
                var cmdInfo = commandsToExecute[i];
                ui.ReportInfo($"[TerminalModule] 正在執行 ({i + 1}/{commandsToExecute.Count}): {cmdInfo.cmd} ...");
                resultBuilder.AppendLine($"--- 執行結果 ({i + 1}/{commandsToExecute.Count}): {cmdInfo.cmd} ---");

                try
                {
                    ProcessStartInfo processStartInfo = new ProcessStartInfo("cmd.exe", "/c " + cmdInfo.cmd)
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using (Process process = new Process())
                    {
                        process.StartInfo = processStartInfo;
                        process.Start();

                        // Read output streams
                        string output = await process.StandardOutput.ReadToEndAsync();
                        string error = await process.StandardError.ReadToEndAsync();

                        // Wait for process to cleanly exit or throw OperationCanceledException
                        await process.WaitForExitAsync(cancellationToken);

                        string result = "";
                        if (!string.IsNullOrWhiteSpace(output))
                        {
                            result += $"[標準輸出]\n{output}\n";
                        }
                        if (!string.IsNullOrWhiteSpace(error))
                        {
                            result += $"[標準錯誤]\n{error}\n";
                        }

                        if (string.IsNullOrWhiteSpace(result))
                        {
                            resultBuilder.AppendLine($"指令已執行完畢 (ExitCode: {process.ExitCode})，無輸出內容。\n");
                        }
                        else
                        {
                            resultBuilder.AppendLine($"指令執行完畢 (ExitCode: {process.ExitCode}):\n{result}");
                        }

                        // if a command fails (optional logic), we could halt. Currently we execute all sequentially.
                        // if (process.ExitCode != 0) { ... }
                    }
                }
                catch (OperationCanceledException)
                {
                    ui.ReportInfo($"[TerminalModule] 執行指令被中斷: {cmdInfo.cmd}");
                    resultBuilder.AppendLine($"[已中斷] 執行指令被系統或使用者取消。\n後續指令已中止。");
                    break; 
                }
                catch (Exception ex)
                {
                    string errMsg = $"[TerminalModule Exception] 執行指令時發生例外: {ex.Message}";
                    ui.ReportError(errMsg);
                    resultBuilder.AppendLine(errMsg + "\n");
                }
            }

            return resultBuilder.ToString();
        }
        private string CheckRestrictedCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return null;

            if (command.Length > 500)
            {
                return "指令長度過長 (超過 500 字元限制)，為確保系統穩定予以攔截";
            }

            string cmdLower = command.Trim().ToLower();

            // 1. 禁止重定向符號與管線符號，新增限制 % (防範環境變數混淆)
            if (cmdLower.Contains(">") || cmdLower.Contains("<") || cmdLower.Contains("|") || cmdLower.Contains("&") || cmdLower.Contains("%"))
            {
                return "禁止使用重定向、管線符號或環境變數 (> < | & %)";
            }

            // 2. 禁止跳轉到父目錄
            if (cmdLower.Contains(".."))
            {
                return "禁止使用 '..' 跳轉父目錄";
            }

            // 3. 白名單檢查
            // 使用 Regex 處理命令中可能出現的連續多餘空白
            string normalizedCmd = System.Text.RegularExpressions.Regex.Replace(cmdLower, @"\s+", " ");

            string[] allowedPrefixes = new[]
            {
                "python ", "python.exe ", 
                "pip ", "pip.exe ",
                "node ", "node.exe ",
                "npm ", "npm.cmd ", "npx ", "npx.cmd ",
                "dir ", 
                "echo ", 
                "type ", 
                "move ", "del ", "ren ", "mkdir ", "rmdir ", "copy ", "rm ", "mv ", "cp ",
                "git status", "git log"
            };

            // 允許完全相等的基礎指令 (沒有參數的情況)
            string[] allowedExact = new[]
            {
                "python", "python.exe",
                "node", "node.exe",
                "npm", "npm.cmd", "npx", "npx.cmd",
                "dir", "echo",
                "git status", "git log"
            };

            bool isAllowed = false;

            // 檢查是否完全符合無參數指令
            foreach (var exact in allowedExact)
            {
                if (normalizedCmd == exact)
                {
                    isAllowed = true;
                    break;
                }
            }

            // 檢查是否以允許的前綴開頭 (有參數的情況)
            if (!isAllowed)
            {
                foreach (var prefix in allowedPrefixes)
                {
                    if (normalizedCmd.StartsWith(prefix))
                    {
                        isAllowed = true;
                        
                        // 針對特定高風險指令進行進階參數檢查
                        // 取出參數部分 (去除前綴)
                        string args = normalizedCmd.Substring(prefix.Length).Trim();

                        if (prefix == "python " || prefix == "python.exe ")
                        {
                            // 檢查 python -m pip install 繞過
                            string[] tokens = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            if (tokens.Length >= 3 && tokens[0] == "-m" && tokens[1] == "pip" && tokens[2] == "install")
                            {
                                return "基於安全考量，禁止使用 'python -m pip install' 繞過 pip 安裝限制";
                            }
                        }
                        else if (prefix == "type ")
                        {
                            // type 只能讀取當前目錄的檔案，不能包含路徑分隔符號
                            if (args.Contains("\\") || args.Contains("/"))
                            {
                                return "基於安全考量，'type' 指令僅允許讀取當前目錄下的檔案，禁止包含路徑分隔符號 (\\ 或 /)";
                            }
                        }
                        else if (prefix == "git log" && normalizedCmd.Length > "git log".Length && normalizedCmd[7] == ' ')
                        {
                            // 將參數拆解成獨立 token，避免誤擋合法的字串 (例如 -pretty)
                            string[] tokens = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            if (tokens.Contains("-p") || tokens.Contains("--patch") || tokens.Contains("--all"))
                            {
                                return "'git log' 指令禁止使用 -p, --patch 或 --all 參數，以避免機敏資訊外洩";
                            }
                        }
                        else if (prefix == "dir ")
                        {
                            // 限制 dir 不能任意列出其他槽的絕對路徑，同時限制路徑跳轉字元
                            if (args.Contains(":\\") || args.Contains(":/") || args.Contains("\\") || args.Contains("/"))
                            {
                                return "'dir' 指令禁止列出其他目錄，請限制在當前所在的工作目錄執行";
                            }
                        }
                        else if (prefix == "npm " || prefix == "npm.cmd " || prefix == "npx " || prefix == "npx.cmd ")
                        {
                            // 檢查 npm 子命令
                            string[] tokens = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            if (tokens.Length > 0)
                            {
                                string subcmd = tokens[0];
                                if (subcmd == "run")
                                {
                                    return "禁止使用 'npm run' 執行自訂腳本，以防止未預期的危害性命令執行";
                                }
                                else if (subcmd == "install" || subcmd == "i")
                                {
                                    if (!tokens.Contains("--ignore-scripts"))
                                    {
                                        return "執行 npm/npx 安裝套件時，必須明確加上 --ignore-scripts 參數防範惡意腳本 (postinstall)";
                                    }
                                }
                            }
                        }
                        else if (prefix == "pip " || prefix == "pip.exe ")
                        {
                            // 檢查 pip 子命令
                            string[] tokens = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            if (tokens.Length > 0)
                            {
                                string subcmd = tokens[0];
                                if (subcmd == "install")
                                {
                                    return "基於安全考量 (防範惡意的 setup.py 執行)，禁止直接執行 'pip install'";
                                }
                            }
                        }
                        
                        break;
                    }
                }
            }

            if (!isAllowed)
            {
                // 取得指令的第一個單詞用於錯誤訊息提示
                string baseCmd = normalizedCmd.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? normalizedCmd;
                return $"此指令不在允許的白名單內 (嘗試執行的指令前綴: {baseCmd})。請參考功能說明中允許的系統指令。";
            }

            return null;
        }
    }
}
