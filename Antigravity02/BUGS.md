# Bug 清單

> 最後更新：2026-03-11（BUG-008 ~ BUG-017 全部已修復）
> 審查範圍：全專案靜態程式碼審查

---

## 🔴 高嚴重度

### BUG-001 — HttpResponseMessage 資源洩漏
- **檔案**：`AIClient/GeminiClient.cs` 約第 61–94 行
- **狀態**：✅ 已修復
- **描述**：`HttpResponseMessage response` 宣告在 `while` 迴圈外，每次 HTTP 429 重試時前一個 `response` 物件未被 `Dispose()`，造成 socket / memory 洩漏。
- **修復方式**：已將 `response` 移入 `using` 區塊，在迴圈內先儲存 `statusCode` 與 `responseJson`，離開 `using` 後再做邏輯判斷。

---

### BUG-002 — ProcessModelPartsAsync 不安全型別轉換與 Null 解參照
- **檔案**：`AIClient/GeminiClient.cs` 約第 253–264 行
- **狀態**：✅ 已修復
- **描述**：
  1. `foreach (Dictionary<string, object> part in parts)` — 若 `parts` 中存在非 `Dictionary` 元素，會丟出 `InvalidCastException`。
  2. `var call = part["functionCall"] as Dictionary<string, object>;` 後直接存取 `call["name"]`，若 `call` 為 null 則丟出 `NullReferenceException`，導致程式崩潰。
- **修復方式**：已改用 `is not` 模式比對跳過非預期型別，並在存取 `call["name"]` 前檢查 `call != null`。

---

### BUG-003 — MockDataManager 靜態 Dictionary 執行緒不安全
- **檔案**：`Tools/MockDataManager.cs` 第 15–16 行
- **狀態**：✅ 已修復
- **描述**：`_mockCounters` 與 `_mockMessageShown` 為靜態 `Dictionary<string, ...>`，在多執行緒環境下並發讀寫未加任何同步保護，可能造成資料損毀或 `KeyNotFoundException`。
- **修復方式**：已改為 `ConcurrentDictionary<string, int>` 與 `ConcurrentDictionary<string, bool>`。

---

## 🟡 中嚴重度

### BUG-004 — IsPathAllowed 未正規化路徑（潛在路徑穿越）
- **檔案**：`Tools/FileTools.cs` 第 47–62 行
- **狀態**：✅ 已修復
- **描述**：`IsPathAllowed` 直接對原始 `targetPath` 加上分隔符後做 `StartsWith` 比對，未先呼叫 `Path.GetFullPath()`。若輸入包含 `..`（例如 `AI_Workspace\..\secret.txt`），可能繞過沙盒限制，讀寫工作區外的檔案。
- **修復方式**：已在比對前對 `targetPath` 與 `allowedBasePath` 都呼叫 `Path.GetFullPath()` 取得正規化絕對路徑後再比對。

---

### BUG-005 — CommandManager Handler 例外未捕捉
- **檔案**：`CommandManager.cs` 約第 117–139 行
- **狀態**：✅ 已修復
- **描述**：`def.Handler(args, out shouldExit)` 若拋出例外，會直接傳播至主迴圈導致程式崩潰，而非顯示錯誤訊息讓使用者繼續操作。
- **修復方式**：已在 Handler 呼叫處包裹 `try-catch`，捕捉後透過 UI 顯示錯誤訊息。

---

### BUG-006 — Ctrl+C 取消被當作一般錯誤處理
- **檔案**：`Agents/BaseAgent.cs` 約第 85–140 行
- **狀態**：✅ 已修復
- **描述**：`catch (Exception ex)` 攔截所有例外，包括使用者按 Ctrl+C 所觸發的 `OperationCanceledException`。取消操作會錯誤地進入錯誤處理流程，觸發不必要的對話歷史備份並顯示錯誤訊息。
- **修復方式**：已在 `catch (Exception ex)` 前加入獨立的 `catch (OperationCanceledException)`，取消時僅顯示「已中斷執行」並直接返回。

---

## 🟢 低嚴重度

### BUG-007 — 未知工具錯誤訊息不含工具名稱（UniversalAgent）
- **檔案**：`Agents/UniversalAgent.cs` 約第 70 行
- **狀態**：✅ 已修復
- **描述**：當 AI 呼叫不存在的工具時，回傳 `"Error: Unknown tool."` 不包含工具名稱，AI 無法從回傳訊息得知是哪個工具不存在，增加排查難度。
- **修復方式**：已改為 `$"Error: Unknown tool '{funcName}'."` 以包含工具名稱。

---

## 🔴 高嚴重度（新發現）

### BUG-008 — ListModelsAsync HttpResponseMessage 資源洩漏
- **檔案**：`AIClient/GeminiClient.cs` 第 187–193 行
- **狀態**：✅ 已修復
- **描述**：`ListModelsAsync` 中 `var response = await _httpClient.GetAsync(url);` 取得的 `HttpResponseMessage` 物件未包在 `using` 區塊內，method 回傳後 `response` 不會被 `Dispose()`，造成 socket / memory 洩漏，與 BUG-001 屬同一類型問題。
- **修復方式**：改用 `using var response = await _httpClient.GetAsync(url);`。

---

## 🟡 中嚴重度（新發現）

### BUG-009 — MockDataManager 計數器遞增競態條件（TOCTOU）
- **檔案**：`Tools/MockDataManager.cs` 第 88–113 行
- **狀態**：✅ 已修復
- **描述**：即使已改用 `ConcurrentDictionary`，以下操作仍非原子：
  1. `ContainsKey` + 索引器賦值（`_mockCounters[normalizedName] = 1`）非原子，兩個執行緒可能同時通過 check 並各自初始化。
  2. `int counter = _mockCounters[normalizedName];` 讀取後，`_mockCounters[normalizedName] = counter + 1;` 賦值前可能被其他執行緒覆蓋，導致計數重複或遺失。
- **修復方式**：初始化改用 `TryAdd`；遞增改用 `AddOrUpdate(key, 2, (_, v) => v + 1)` 確保原子性。

---

### BUG-010 — GenerateSummaryAsync 忽略 CancellationToken
- **檔案**：`Agents/BaseAgent.cs` 第 436 行
- **狀態**：✅ 已修復
- **描述**：歷史壓縮流程 `GenerateSummaryAsync` 呼叫 `FastClient.GenerateContentAsync(request)` 時未傳入 `cancellationToken`。使用者按 Ctrl+C 後，主流程已收到取消訊號並 break，但此處的 API 請求仍會繼續執行至完成（或逾時），浪費 API 配額並延遲關閉。
- **修復方式**：`GenerateSummaryAsync`、`CompressHistoryAsync`、`HandleTokenUsageAsync` 整條呼叫鏈均加入 `CancellationToken` 參數並逐層傳遞至 `GenerateContentAsync`。

---

## 🟢 低嚴重度（新發現）

### BUG-011 — MultiAgentModule 未知工具錯誤訊息遺漏工具名稱
- **檔案**：`Agents/Modules/MultiAgentModule.cs` 第 347 行
- **狀態**：✅ 已修復
- **描述**：在專家 function call 迴圈中，`if (result == null) result = "Error: Unknown tool.";` 不包含工具名稱，與 BUG-007（已修復）屬同一問題，但 `MultiAgentModule` 尚未同步修正。
- **修復方式**：改為 `$"Error: Unknown tool '{funcName}'."` 以包含工具名稱。

---

## 🔴 高嚴重度（第三輪審查）

### BUG-012 — HandleReadImage 圖片路徑未經沙盒驗證（路徑穿越）
- **檔案**：`Agents/Modules/FileModule.cs` 第 264–316 行
- **狀態**：✅ 已修復
- **描述**：`HandleReadImage` 對圖片路徑只做了 `AI_Workspace/` 前綴去除，未呼叫 `FileTools.IsPathAllowed` 沙盒驗證，存在路徑穿越問題：
  1. 若路徑已是絕對路徑（如 `C:\Windows\secret.png`），`File.Exists(cleanedPath)` 直接回傳 `true`，檔案被讀取且 Base64 後送入 AI。
  2. 若路徑含 `..`（如 `../../secret.png`），`Path.GetFullPath(Path.Combine(aiWorkspacePath, "../../secret.png"))` 可能指向工作區外，並沒有邊界驗證。
- **建議修復**：在呼叫 `File.ReadAllBytes` 前，用 `Path.GetFullPath` 正規化路徑，確認路徑起始於 `aiWorkspacePath`（與 `FileTools.IsPathAllowed` 邏輯相同）。

---

## 🟡 中嚴重度（第三輪審查）

### BUG-013 — CancellationTokenSource 資源洩漏
- **檔案**：`Program.cs` 第 133 行
- **狀態**：✅ 已修復
- **描述**：主互動迴圈 `RunInteractiveLoopAsync` 每次輸入都 `_currentCts = new CancellationTokenSource();`，但上一輪的 `CancellationTokenSource` 從未呼叫 `Dispose()`，導致持續累積的資源洩漏（包含 WaitHandle 等非管理資源）。
- **建議修復**：在建立新 CTS 前，先對舊的呼叫 `_currentCts?.Dispose()`。

---

### BUG-014 — SummarizeContentAsync 缺乏 null 防護
- **檔案**：`Agents/Modules/FileModule.cs` 第 491–499 行
- **狀態**：✅ 已修復
- **描述**：解析摘要回應時，`data["candidates"]`（若 key 不存在拋 KeyNotFoundException）、`(candidates[0] as Dictionary...)["content"]`（若轉型失敗為 null，則 null["content"] 拋 NullReferenceException）等操作未加防護。雖然包在 `catch` 中不會崩潰，但所有非預期格式的回應均導致摘要靜默失敗，退而回傳 `[Fast AI Error]`。
- **建議修復**：改用與 `ProcessModelPartsAsync` 相同的防禦式存取模式（`is` 型別檢查 + null 判斷）。

---

## 🟢 低嚴重度（第三輪審查）

### BUG-015 — LogApiError 使用 new Random() 可能產生重複錯誤日誌檔名
- **檔案**：`Tools/UsageLogger.cs` 第 94 行
- **狀態**：✅ 已修復
- **描述**：`new Random().Next(1000, 9999)` 在快速連續呼叫時（如多次 429 重試），多個 `Random` 實例可能基於相同的毫秒種子產生相同數字，加上秒級時間戳相同，生成重複的檔案名稱，後者覆蓋前者，錯誤日誌遺失。
- **建議修復**：改用 `Guid.NewGuid().ToString("N").Substring(0, 8)` 取代隨機數部分，保證唯一性。

---

### BUG-016 — UpdateEnvWithModelListAsync 不安全型別轉換
- **檔案**：`Program.cs` 第 226–228 行
- **狀態**：✅ 已修復
- **描述**：`foreach (Dictionary<string, object> model in models)` 直接強制轉型 `ArrayList` 元素，若 API 回傳非 `Dictionary` 的元素，拋出 `InvalidCastException`（被外層 `catch {}` 靜默吞掉，導致模型清單無法寫入 `.env`）。此外 `model["name"]` 和 `model["displayName"]` 直接存取也缺乏 key 存在性與 null 值防護。
- **建議修復**：改用 `if (model is not Dictionary<string, object> m) continue;` 跳過非預期元素，並用 `TryGetValue` 安全存取 key。

---

### BUG-017 — PromptContinueAsync 在非同步上下文使用同步等待（潛在死鎖）
- **檔案**：`UI/ConsoleUI.cs` 第 54 行
- **狀態**：✅ 已修復
- **描述**：`PromptContinueAsync` 內部以 `.GetAwaiter().GetResult()` 同步等待 `PromptSelectionAsync`。目前 `PromptSelectionAsync` 回傳 `Task.FromResult` 不會死鎖，但此模式違反非同步設計原則：若未來 `PromptSelectionAsync` 改為真正的非同步實作，在有 `SynchronizationContext` 的執行環境（如 ASP.NET）下將導致死鎖。
- **建議修復**：將 `PromptContinueAsync` 改為 `async` 方法，使用 `await PromptSelectionAsync(...)`。
