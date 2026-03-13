using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Antigravity02.AIClient;
using Antigravity02.UI;

namespace Antigravity02.Agents
{
    public class TerminalModule : BaseAgentModule
    {
        protected override IEnumerable<object> BuildToolDeclarations(IAIClient client)
        {
            yield return client.CreateFunctionDeclaration(
                "run_terminal_command",
                "【系統：執行終端指令】依序執行多個作業系統的命令列指令(如 cmd.exe /c 的內容)。此操作因為具有高度風險，系統必定會先暫停並詢問使用者是否同意執行。只有當使用者同意時才會依序執行所有指令並回傳結果。",
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
    }
}
