# XivMark

<img src="images/icon.png" width="96" height="96" alt="XivMark icon">

Dalamud API 15 插件：在 **Frontline（紛爭前線）** 自動為指定職業的敵方玩家放置遊戲原生頭頂標記。預設暗黑騎士（DRK）及舞者（DNC）。

## 自訂設定

輸入 `/xivmark` 開啟設定。修改會立即保存，重新載入後保留。由舊版升級會保留開關、距離及數量，新選項沿用原本行為。

v0.4.0 使用原生 Dalamud 樣式，開關及運作狀態固定在頂部，設定分成四個分頁：

- **General**：距離、時間、數量及清標規則。
- **Jobs**：按坦克、補師、近戰、遠程／法系分組勾選；滑鼠停留顯示職業名稱。
- **Markers**：啟用勾選、順序、目前持有人／確認狀態，以及 Up／Down 排序。
- **Diagnostics**：玩家觀察表、敵人篩選及引擎資訊。

說明主要放在滑鼠提示，分頁內容可捲動。更換介面不修改原本的自動標記及清標判斷。

| 設定 | 可調範圍／作用 | 預設 |
| --- | --- | --- |
| Marking range | 新標記目標的距離，10–100 yalms | 60 |
| Maximum marker slots | 最多使用多少個已啟用標記，0–13 | 13 |
| Mark / clear interval | 標記與清標共用的操作間隔，1–10 秒 | 1 秒 |
| Delay after entering Frontline | 進入場景後等 0–120 秒才開始操作 | 0 秒 |
| Only add marks to enemies in combat | 只為已進入戰鬥的敵人新增標記 | 關閉 |
| Pause while I am dead | 自己死亡時暫停新增標記及清標，復活後恢復 | 關閉 |
| Clear marks on unselected enemy jobs | 檢查現有標記，清除未勾選職業的敵人身上的標記，包括其他來源或重新載入前留下的 | 開啟 |
| Jobs 分頁 | 勾選要追蹤的戰鬥職業 | DRK、DNC |
| Markers 分頁 | 個別勾選標記，使用 Up／Down 調整順序 | 全部 13 個 |

入場延遲由本次 Frontline 場景第一次被偵測到時計算，開關插件不會重新倒數；重新載入插件會重新計時。視窗及 `/xivmark status` 會顯示等待秒數或死亡暫停原因。暫停不會清除標記，已送出的操作仍會確認狀態。

距離、戰鬥篩選及標記順序不會搬動原有的合資格標記。若開啟職業清標，**取消勾選某職業也會清除啟用範圍內該職業敵人身上的標記，不限來源**。關閉職業清標即可保留。全部職業或標記均不勾選時，不新增標記；如需完全停止所有操作，使用 `/xivmark off`。

## 預設行為

預設敵方 DRK／DNC **共用同一組 13 個標記位**，不按職業分開。新目標按距離由近至遠選取，預設依序使用：

| 標記 | 數量 |
| --- | --- |
| Attack 1–8 | 8 |
| Chain／Bind 1–3 | 3 |
| Stop／Ignore 1–2 | 2 |

- 首次載入預設關閉；用 `/xivmark` 開設定，或 `/xivmark on` 開啟。
- 預設範圍 60 yalms，可調 10–100；共用標記位數量預設 13，可調 0–13，使用自訂順序中已啟用的前 N 個位置。減少數量或取消啟用標記只限制新增標記，不清除已有標記。
- 只使用遊戲已載入、存活、可選取的敵方玩家。先排除隊伍／聯盟成員，再用遊戲的 `GetTargetType() == Enemy` 判斷敵我；不使用角色所屬大國防聯軍作判斷。
- 僅在 `TerritoryIntendedUse = 18` 並且處於 PvP 時運作，不會在水晶衝突、烈羽爭鋒、狼獄或 PvE 使用。
- **自動 mark，但不是一瞬間全部放。** 每 250ms 檢查一次，依設定間隔（預設一秒）送出一個操作，而且要先觀察上一個標記的狀態更新。預設由第一個到第十三個至少相隔 12 秒；實際時間受可用目標及狀態更新影響。
- **符合已選職業的標記固定保留；開啟職業清標時，現有標記落在未選職業的敵人身上就優先清除。** 在已選職業之間互轉保留。死亡、走遠、離開載入範圍、退出戰鬥或不可選取本身不會觸發清標；友方、未知職業（0）及未載入玩家均保留。
- 清標掃描設定中已啟用且受數量上限涵蓋的位置，也會繼續處理本次插件原本持有但後來停用的位置。其他人放的、重新載入前留下的標記也會按職業檢查；不會因缺少內部持有紀錄漏清。形狀標記仍不在本插件範圍。清標前再次核對持有人、敵我及職業，確認清除後才釋放位置；距離／戰鬥篩選不限制清標。
- 讀取全部 17 個標記位，跳過已佔用的標記位及已被任何標記標中的目標。
- 遊戲本身或其他玩家也可以移除／更改標記。觀察到外部修改後，原目標不自動重放，也不換另一個標記標同一人。讓出的位置若仍有未選職業敵人的標記，仍按職業清標規則處理；這種刻意清理成功後可釋放位置。
- 13 個位置用完後就等待；確認未選職業並完成清標後，位置才可回收。標記保留及讓出紀錄只在目前插件執行期間有效；離場或重新載入會重設內部紀錄。重新載入後掃描現有標記：合資格職業保留、未選職業敵人清除。
- 這是根據本機觀察的最佳努力協調；同時有多個自動標記插件時，不能保證完全沒有競爭。
- 不改變你的目前目標，不發送聊天訊息，不需要 PostNamazu 或其他插件。

## 安裝

在 `/xlsettings` → **Experimental** → **Custom Plugin Repositories** 加入：

```text
https://raw.githubusercontent.com/kuchris/DalamudPlugins/main/repo.json
```

啟用並儲存，再到 `/xlplugins` 搜尋 **XivMark** 安裝。輸入 `/xivmark` 開設定，啟用自動標記。

[Source](https://github.com/kuchris/xivmark) · [Releases](https://github.com/kuchris/xivmark/releases)

如果之前用 Dev Plugin Locations 載入本機版，先停用本機版再安裝插件庫版本，避免載入兩份。

## 本機開發載入

需要支援 API 15 的 Dalamud。已使用本機安裝的 API 15 assemblies 編譯。

1. 在遊戲輸入 `/xlsettings`，進入 **Experimental** 的 **Dev Plugin Locations**。
2. 新增 DLL 完整路徑並儲存：

   ```text
   C:\path\to\xivmark\XivMark\bin\Release\XivMark.dll
   ```

3. 在 `/xlplugins` 的開發插件清單載入 **XivMark**。
4. 輸入 `/xivmark` 調整設定，再勾選 **Enable automatic marking**。

發佈用壓縮檔：`artifacts/XivMark-0.4.0.0.zip`。解壓後也可以將解壓位置的 `XivMark.dll` 加入 Dev Plugin Locations；DLL 與 JSON 必須保留在同一資料夾。已使用上面的本機 DLL 路徑時，重新載入插件即可使用新版，視窗頂部會顯示版本。

GitHub release 提供 ZIP；一般安裝建議使用上面的插件庫。

## 指令與停止

| 指令 | 行為 |
| --- | --- |
| `/xivmark` | 開設定及診斷視窗 |
| `/xivmark on` | 開啟自動標記，設定會保存 |
| `/xivmark off` | 停止新增標記，保留已有標記 |
| `/xivmark status` | 在自己的聊天視窗及日誌顯示版本、等待原因、持有／讓出／未確認位置數量及合資格敵人數量 |

關閉或卸載插件均不會因停用而清標，停用也會停止新增轉職清標請求。如需清除，先 `/xivmark off`，待已送出的請求完成後用遊戲原生標記操作手動清除。離開 Frontline 後只重設內部狀態，不送出清標命令。

若找不到原生函式簽名，插件不會標記。若發出操作後四秒仍未觀察到標記狀態更新，只會將該位置及目標保留為 `Unconfirmed`，**其他可用位置繼續運作**。不重試相同指令，避免 toggle 清掉已經放好的標記。之後觀察到遲到的更新，會恢復該位置的持有紀錄；遲到的轉職清標更新則釋放位置。若位置被另一人使用，依照原有規則讓出。

視窗頂部持續顯示實際等待原因，包括操作間隔、入場延遲、自己死亡、沒有合資格敵人、所有敵人已被標記、位置已滿／讓出／未確認。未確認的位置用完時會等待，不會回收並重試。`Last warning` 是上一次逾時紀錄，不代表目前全部停止；以當前狀態及 `Unconfirmed` 數量為準。

13 個位置均已有固定標記時，不再標記新出現的人是預期行為。沒有觀察到目標或超出範圍，也不會為了新目標轉移舊標記。

## 驗證狀態

2026-10-25，v0.4.0.0：原生介面分頁重整；Release 編譯成功，0 warnings / 0 errors；原有 234 項離線檢查通過。自動標記及清標核心未改動。

遇到中途停止時，`/xivmark status` 可將版本、等待原因及計數寫入本機日誌，協助區分目標篩選、標記位已滿及未確認操作。

使用者已回報 v0.3.2 的功能可以工作；v0.4.0 的介面尚未在遊戲內完成視覺與互動驗證。編譯及離線檢查不代表已驗證所有遊戲情況或隊友可見性。標記狀態確認來自本機 `MarkingController`，不是伺服器或其他玩家的確認。

首次入場請檢查：

1. 外面顯示 `Idle - outside Frontline`；進 Frontline 顯示 `Frontline: True`。
2. 診斷列表中，己方顯示 `friendly/unknown`，敵方顯示 `enemy`。列表顯示已載入的所選職業，以及任何身上有標記的玩家，方便查看未選職業清標情況。
3. 建議初次把共用數量設為 2，確認附近的敵方 DRK／DNC 依序取得 Attack 1、Attack 2，並由隊友確認看到標記；之後調到 13 檢查 Attack 6–8、Chain 1–3、Stop 1–2。
4. 確認友方及已選職業的原有標記保留；敵人死亡或走遠本身不觸發清標，關閉也保留標記。如果遊戲自動清標，確認插件不重放或轉給另一人。
5. 確認 DRK ↔ DNC 互轉保留標記；啟用位置的標記在未選職業敵人身上時會清除並釋放位置，包括先前已存在或別人放置的標記。重新載入插件後也應執行相同檢查。
6. 調整操作間隔、入場延遲、戰鬥篩選及死亡暫停；確認等待狀態及復活後恢復符合設定。自訂職業及標記順序後，重新載入確認設定有保存。
7. 如出現中途不標記，先看視窗頂部等待原因及 `Unconfirmed` 數量；`/xivmark status` 會把版本及原因同時寫入本機日誌。如出現錯標或錯誤，先 `/xivmark off`，保留 `/xllog` 及診斷視窗狀態以便排查。

## 開發

```powershell
dotnet build XivMark.slnx -c Release
dotnet run --project XivMark.Tests/XivMark.Tests.csproj -c Release
dotnet run --project XivMark.Tests/XivMark.Tests.csproj -c Release -- --repro-timeout
dotnet run --project XivMark.Tests/XivMark.Tests.csproj -c Release -- --repro-unselected-marker
```

在 Windows 建立並驗證 release ZIP、icon 與 SHA256：

```powershell
pwsh -File tools/Build-Release.ps1
```

需要 .NET SDK 10 和 Dalamud API 15 開發檔案。`Dalamud.NET.Sdk/15.0.0` 預設從 `%APPDATA%\XIVLauncher\addon\Hooks\dev` 讀取 assemblies，也支援 `DALAMUD_HOME`。

`XivMark.Core/MarkerPlanner.cs` 不依賴 Dalamud；測試涵蓋篩選、全 DRK／全 DNC／混合目標共用 13 個位置、Attack 6–8 的非連續 native ID、固定標記、轉職清理及回收、未知職業保留、延遲回覆、超時、停用保留、人工覆寫及離場重設。`NativeMarkers.cs` 隔離遊戲函式呼叫，清標前再次核對職業及標記，並拒絕直接覆蓋及形狀標記請求；`Plugin.cs` 負責框架執行緒、物件快照及設定 UI。

原生介面的核對來源：

- [FFXIVClientStructs MarkingController](https://github.com/aers/FFXIVClientStructs/blob/main/FFXIVClientStructs/FFXIV/Client/Game/UI/MarkingController.cs)：讀取標記陣列；不直接寫入陣列。
- [Lemegeton Core/State.cs](https://github.com/paissaheavyindustries/Lemegeton/blob/master/Lemegeton/Core/State.cs)：核對 native marking entry point 的簽名及一基底 marker ID。本專案自行實作排程及篩選，沒有依賴 Lemegeton。

遊戲更新後應重新核對原生介面及完成入場測試。
