# CLAUDE.md

此檔案提供 Claude Code (claude.ai/code) 在此儲存庫中工作時的指引。

## 建置與執行

```bash
dotnet build                    # 除錯建置
dotnet build -c Release         # 發布建置
dotnet run                      # 互動模式
dotnet run -- "你的提示"         # 單次執行
```

目前無測試專案，也無 lint 設定。

## 環境設定

應用程式首次執行時會自動產生 `.env` 檔案。必填金鑰：
- `GEMINI_API_KEY` — Gemini API 認證金鑰

選填金鑰：
- `GEMINI_SMART_MODEL` — 預設為 `gemini-2.5-flash`
- `GEMINI_FAST_MODEL` — 預設為 `gemini-2.5-flash`

自訂系統指令請放在 `.agent/SystemInstruction.txt`。

## 架構

Antigravity02 是一個以 Google Gemini 為後端的主控台 AI 自動化助手。它執行迭代式函式呼叫迴圈，由模型自行選擇要呼叫的工具，收集結果後繼續執行，直到產生最終文字回應為止（最多 30 次迭代）。

### 核心流程

```
使用者輸入 → CommandManager (斜線指令) 或 UniversalAgent.ExecuteAsync()
                                                    │
                                          BaseAgent 函式呼叫迴圈
                                          (建立請求 → GeminiClient → 解析回應)
                                                    │
                                 模組派送 (FileModule / HttpModule / AIControlModule / MultiAgentModule)
                                                    │
                                          回傳結果 → 繼續迴圈或輸出最終回應
```

### 主要元件

| 檔案 | 職責 |
|------|------|
| `Program.cs` | 進入點；處理 CLI 參數、`.env` 初始化、互動式 REPL、Ctrl+C 中斷 |
| `CommandManager.cs` | 註冊斜線指令（`/exit`, `/help`, `/new`, `/save`, `/load`, `/time`, `/rmock`） |
| `Agents/BaseAgent.cs` | 抽象基底類別：函式呼叫迴圈、對話歷史管理、Token 壓縮（超過 80 萬 token 時自動摘要）、模型模式協調 |
| `Agents/UniversalAgent.cs` | 具體 Agent 實作，整合所有模組並初始化工具 |
| `Agents/Modules/FileModule.cs` | 檔案操作（列出/讀取/寫入/刪除/移動）、影像辨識、技能與知識庫存取 |
| `Agents/Modules/HttpModule.cs` | HTTP GET/POST 請求 |
| `Agents/Modules/AIControlModule.cs` | 執行期模型切換（Smart ↔ Fast）、行為自我調整、系統指令更新 |
| `Agents/Modules/MultiAgentModule.cs` | 建立獨立專家 Agent 會話、非同步任務編排 |
| `AIClient/GeminiClient.cs` | Gemini API 客戶端；處理 429 退避重試、模擬資料回放 |
| `Tools/FileTools.cs` | 沙盒化 I/O，限制在 `AI_Workspace/` 內；路徑安全性強制執行 |
| `Tools/MockDataManager.cs` | API 回應的錄製與重播，供離線開發使用（`/rmock` 指令） |
| `Config/AgentConfig.cs` | 系統指令範本；與 `.agent/SystemInstruction.txt` 合併 |
| `UI/ConsoleInputHelper.cs` | 輸入歷史記錄與斜線指令自動補全 |

### 雙模型設計

Agent 支援兩種可在執行期切換的模型模式：
- **Smart** — 著重推理，用於複雜任務
- **Fast** — 著重速度，用於快速回應

`AIControlModule` 提供 `switch_model` 工具，讓 AI 可在對話途中自行切換模式。

### 檔案沙盒

所有 AI 檔案操作均限制在 `AI_Workspace/` 目錄內。`.agent/` 子目錄儲存持久化的 Agent 狀態：
- `.agent/skills/` — 學習到的技能腳本
- `.agent/knowledge/` — 知識庫檔案
- `.agent/rules/` — 行為規則

### 對話歷史壓縮

當對話歷史超過約 80 萬 token 時，`BaseAgent` 會自動壓縮：將完整歷史傳送給模型進行摘要，並以結構化 XML（`<Summary>` / `<Knowledge>` 標籤）取代較舊的輪次，同時保留最近的對話內容。

### 日誌記錄

- `logs/` — 每日 API 使用日誌（token 數、耗時、模型名稱）
- `err/` — 錯誤詳細日誌，包含請求/回應內容與唯一識別碼
