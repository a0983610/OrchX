# Bug 清單

> 最後更新：2026-03-10
> 審查範圍：全專案靜態程式碼審查

---

## 🔴 高嚴重度

### BUG-001 — HttpResponseMessage 資源洩漏
- **檔案**：`AIClient/GeminiClient.cs` 約第 61–94 行
- **狀態**：未修復
- **描述**：`HttpResponseMessage response` 宣告在 `while` 迴圈外，每次 HTTP 429 重試時前一個 `response` 物件未被 `Dispose()`，造成 socket / memory 洩漏。
- **建議修復**：將 `response` 移入 `using` 區塊，在迴圈內先儲存 `statusCode` 與 `responseJson`，離開 `using` 後再做邏輯判斷。

---

### BUG-002 — ProcessModelPartsAsync 不安全型別轉換與 Null 解參照
- **檔案**：`AIClient/GeminiClient.cs` 約第 253–264 行
- **狀態**：未修復
- **描述**：
  1. `foreach (Dictionary<string, object> part in parts)` — 若 `parts` 中存在非 `Dictionary` 元素，會丟出 `InvalidCastException`。
  2. `var call = part["functionCall"] as Dictionary<string, object>;` 後直接存取 `call["name"]`，若 `call` 為 null 則丟出 `NullReferenceException`，導致程式崩潰。
- **建議修復**：改用 `is not` 模式比對跳過非預期型別，並在存取 `call["name"]` 前檢查 `call != null`。

---

### BUG-003 — MockDataManager 靜態 Dictionary 執行緒不安全
- **檔案**：`Tools/MockDataManager.cs` 第 15–16 行
- **狀態**：未修復
- **描述**：`_mockCounters` 與 `_mockMessageShown` 為靜態 `Dictionary<string, ...>`，在多執行緒環境下並發讀寫未加任何同步保護，可能造成資料損毀或 `KeyNotFoundException`。
- **建議修復**：改為 `ConcurrentDictionary<string, int>` 與 `ConcurrentDictionary<string, bool>`。

---

## 🟡 中嚴重度

### BUG-004 — IsPathAllowed 未正規化路徑（潛在路徑穿越）
- **檔案**：`Tools/FileTools.cs` 第 47–62 行
- **狀態**：未修復
- **描述**：`IsPathAllowed` 直接對原始 `targetPath` 加上分隔符後做 `StartsWith` 比對，未先呼叫 `Path.GetFullPath()`。若輸入包含 `..`（例如 `AI_Workspace\..\secret.txt`），可能繞過沙盒限制，讀寫工作區外的檔案。
- **建議修復**：在比對前對 `targetPath` 與 `allowedBasePath` 都呼叫 `Path.GetFullPath()` 取得正規化絕對路徑後再比對。

---

### BUG-005 — CommandManager Handler 例外未捕捉
- **檔案**：`CommandManager.cs` 約第 117–139 行
- **狀態**：未修復
- **描述**：`def.Handler(args, out shouldExit)` 若拋出例外，會直接傳播至主迴圈導致程式崩潰，而非顯示錯誤訊息讓使用者繼續操作。
- **建議修復**：在 Handler 呼叫處包裹 `try-catch`，捕捉後透過 UI 顯示錯誤訊息。

---

### BUG-006 — Ctrl+C 取消被當作一般錯誤處理
- **檔案**：`Agents/BaseAgent.cs` 約第 85–140 行
- **狀態**：未修復
- **描述**：`catch (Exception ex)` 攔截所有例外，包括使用者按 Ctrl+C 所觸發的 `OperationCanceledException`。取消操作會錯誤地進入錯誤處理流程，觸發不必要的對話歷史備份並顯示錯誤訊息。
- **建議修復**：在 `catch (Exception ex)` 前加入獨立的 `catch (OperationCanceledException)`，取消時僅顯示「已中斷執行」並直接返回。

---

## 🟢 低嚴重度

### BUG-007 — 未知工具錯誤訊息不含工具名稱
- **檔案**：`Agents/UniversalAgent.cs` 約第 70 行
- **狀態**：未修復
- **描述**：當 AI 呼叫不存在的工具時，回傳 `"Error: Unknown tool."` 不包含工具名稱，AI 無法從回傳訊息得知是哪個工具不存在，增加排查難度。
- **建議修復**：改為 `$"Error: Unknown tool '{funcName}'."` 以包含工具名稱。
