Antigravity02 - C# 萬能 AI 代理系統
專案簡介
Antigravity02 是一個基於 .NET Framework 4.7.2 開發的終端機 AI Agent 應用程式。本專案整合了 Google Gemini API，並實作完整的 Function Calling 機制。透過此架構，AI 具備操作本機檔案、發送網路請求以及動態調度多領域專家的能力，旨在打造一個可自我擴充與維護的自動化工作站。

核心架構與功能
系統採用模組化 (Modular) 設計，包含以下核心能力：

1. 模組化工具箱 (Agent Modules)
檔案系統沙盒 (FileModule)：提供受限於 AI_Workspace 目錄內的檔案操作，包含讀寫檔案、單行更新 (update_file_line)、關鍵字搜尋與搬移。此外，支援讀取圖片並自動轉為 Base64 注入 AI 的視覺輸入。

網路請求 (HttpModule)：提供標準的 http_get 與 http_post 功能，允許 AI 直接與外部 API 或網頁互動。

技能與知識庫管理：AI 可透過 write_skill 建立自訂的 Markdown 格式標準作業流程 (SOP)，並能透過 write_note 將重要資訊寫入知識庫，系統會自動維護 00_INDEX.md 索引供 AI 隨時檢索。

2. 多專家協作系統 (Multi-Agent System)
動態專家調度：主 Agent 可透過 consult_expert 工具建立並呼叫具備獨立角色設定 (Role) 與對話歷史的子專家 Session。

非同步任務執行：支援將耗時的複雜任務交由專家在背景非同步執行 (is_async=true)。主 Agent 可繼續與使用者對話，並在稍後透過 check_task_status 查詢專家的執行結果。

3. 資源與上下文管理
雙模型動態切換：系統定義了 Smart (推理能力強) 與 Fast (速度快/成本低) 兩種模型模式。AI 可根據任務的複雜度，自行呼叫 switch_model_mode 切換運作模式。

自動歷史壓縮機制：當單次對話累積的 Token 數量超過預設閾值 (預設為 80 萬 Token) 時，系統會自動在背景啟動 Fast 模型，對前半段對話歷史進行摘要與關鍵資訊萃取，藉此釋放上下文空間並避免記憶遺失。

4. 開發與除錯輔助
Mock 測試模式：在未設定 API Key 或需要離線測試時，系統可自動讀取 MockData/ 目錄下的 JSON 模擬回應。開發者也可透過指令開啟錄製模式，將真實的 API 回應存為 Mock Data。

異常恢復：若程式執行中發生嚴重錯誤或被強制中斷，系統會自動將當前的對話紀錄備份為 JSON 檔案，方便後續載入重試。

快速上手
1. 初始化與配置
首次執行 Antigravity02.exe 時，系統會自動於程式根目錄產生 .env 設定檔。請在 .env 中填寫您的 API 資訊：

程式碼片段
GEMINI_API_KEY=你的_API_KEY_填寫於此
GEMINI_SMART_MODEL=gemini-2.5-pro
GEMINI_FAST_MODEL=gemini-2.5-flash
(若未填寫 API Key，系統將以 Mock 模式啟動並讀取既有的測試資料。)

2. 系統指令 (CLI Commands)
在終端機介面中，您可以直接以自然語言與 AI 對話，或使用以下斜線指令控制系統：

/new：清除當前對話歷史，開啟全新任務。

/save [path]：將當前對話紀錄匯出儲存為 JSON 檔案。

/load [path]：載入先前的對話紀錄檔案。

/time：開關訊息的時間戳記標頭 (讓 AI 具備時間感知)。

/rmock：開關 API 回應錄製模式，開啟後會將真實 API 回應存入 MockData 目錄。

/help：顯示系統指令說明。

/exit：安全結束程式。

安全性與邊界限制
路徑限制：所有的檔案讀寫與搜尋操作，皆被程式碼層級嚴格限制在 AI_Workspace/ 目錄及其子目錄下。系統會主動阻擋任何包含 .. 的越權路徑存取嘗試。
狀態暫留：Agent 的當前對話狀態預設存在於記憶體中，程式關閉即釋放。若有重要的發現或設定，請指示 AI 使用 write_note 將其持久化寫入實體檔案中。

---

Antigravity02 - Universal AI Agent System in C#
Project Overview
Antigravity02 is a terminal-based AI Agent application built on .NET Framework 4.7.2. This project integrates the Google Gemini API and implements a complete Function Calling mechanism. Through this architecture, the AI is equipped to operate local files, send network requests, and dynamically orchestrate multi-domain experts, aiming to build a self-extensible and maintainable automated workstation.

Core Architecture & Features
The system adopts a modular design, encompassing the following core capabilities:

1. Modular Toolset (Agent Modules)
File System Sandbox (FileModule): Provides restricted file operations within the AI_Workspace directory, including reading/writing files, single-line updates (update_file_line), keyword searching, and moving files. Additionally, it supports reading images and automatically converting them to Base64 to inject into the AI's visual input.

Network Requests (HttpModule): Provides standard http_get and http_post functions, allowing the AI to interact directly with external APIs or web pages.

Skills & Knowledge Base Management: The AI can create custom Markdown-formatted Standard Operating Procedures (SOPs) via write_skill. It can also write important information into the knowledge base using write_note, and the system will automatically maintain a 00_INDEX.md index for the AI to retrieve at any time.

2. Multi-Agent System
Dynamic Expert Orchestration: The main Agent can spawn and invoke sub-expert sessions with independent roles and conversation histories using the consult_expert tool.

Asynchronous Task Execution: Supports delegating time-consuming, complex tasks to experts to run asynchronously in the background (is_async=true). The main Agent can continue conversing with the user and query the expert's execution result later via check_task_status.

3. Resource & Context Management
Dynamic Dual-Model Switching: The system defines two model modes: Smart (high reasoning capability) and Fast (high speed/low cost). The AI can autonomously call switch_model_mode to switch operating modes based on task complexity.

Automatic History Compression: When the accumulated token count of a single conversation exceeds a predefined threshold (default is 800,000 tokens), the system automatically launches the Fast model in the background to summarize and extract key information from the earlier half of the conversation history. This frees up context space and prevents memory loss.

4. Development & Debugging Aids
Mock Testing Mode: When no API Key is set or offline testing is required, the system can automatically read JSON mock responses from the MockData/ directory. Developers can also use commands to enable recording mode, saving real API responses as Mock Data.

Error Recovery: If a critical error occurs or the program is forcibly interrupted during execution, the system will automatically back up the current conversation history as a JSON file for easy reloading and retrying later.

Getting Started
1. Initialization & Configuration
When running Antigravity02.exe for the first time, the system will automatically generate a .env configuration file in the program's root directory. Please fill in your API information in the .env file:

程式碼片段
GEMINI_API_KEY=Your_API_KEY_Here
GEMINI_SMART_MODEL=gemini-2.5-pro
GEMINI_FAST_MODEL=gemini-2.5-flash
(If the API Key is left blank, the system will start in Mock mode and read existing test data.)

2. CLI Commands
In the terminal interface, you can converse with the AI directly using natural language or use the following slash commands to control the system:

/new: Clears the current conversation history and starts a new task.

/save [path]: Exports and saves the current conversation history as a JSON file.

/load [path]: Loads a previous conversation history file.

/time: Toggles the timestamp header for messages (giving the AI time awareness).

/rmock: Toggles the API response recording mode. When enabled, real API responses are saved to the MockData directory.

/help: Displays the system command help menu.

/exit: Safely exits the program.

Security & Boundaries
Path Restrictions: All file read/write and search operations are strictly restricted at the code level to the AI_Workspace/ directory and its subdirectories. The system actively blocks any unauthorized path access attempts containing ...

State Persistence: The Agent's current conversation state defaults to existing only in memory and is released when the program closes. If there are important findings or configurations, instruct the AI to use write_note to persist them into physical files.
