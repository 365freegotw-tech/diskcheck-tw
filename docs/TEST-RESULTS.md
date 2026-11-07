# 測試結果（2026-11-07）

## 0.1.3 Linux 驗證

`dotnet build -c Release`：0 warning、0 error。`dotnet test -c Release`：79 passed、0 failed、0 skipped（含排程 XML、月份、月底日期、錯過門檻及略過週期）。Windows 工作排程器與實體磁碟須在 Windows 實測。

### 0.1.3 Windows 待測

- [ ] 安裝版建立、修改及停用排程；檢查目前使用者、最高可用權限與不跳 UAC
- [ ] 準時執行、逐顆 SMART＋快速／完整掃描、進度取消與報告內容
- [ ] 關機錯過後登入提示；「稍後」於下次登入再問；「略過」推進週期
- [ ] 使用電池時不執行，改接交流電後的行為
- [ ] 非系統管理員帳號的權限提示與排程結果
- [ ] 解除安裝移除排程及 schedule.json，保留個人報告

## 早期版本驗證紀錄

Linux 建置環境：.NET SDK 10.0.403；目標為 Windows 10/11 x64。Linux 執行交叉編譯與單元測試。

| 檢查 | 實際結果 |
| --- | --- |
| `dotnet build -c Release` | 通過；0 error、1 warning（既有 `HealthAnalyzerTests.cs` 的 xUnit2012） |
| `dotnet test tests/DiskCheckTW.Tests -c Release` | 通過；45 passed、0 failed、0 skipped；包含新加入的 `ReportGeneratorTests.cs` |
| `build/publish.sh` | 通過；產出 `publish/DiskCheckTW/DiskCheckTW.App.exe`、0.1.1 免安裝 ZIP 與 NSIS 安裝檔 |
| ZIP 完整性 | `python3 -m zipfile -t` 通過 |
| 安裝程式 | 本機 NSIS 編譯通過；Windows 安裝與解除安裝尚未實測 |
| 安全字串檢查 | 原始碼檔中無 `WriteFile`、`FSCTL_LOCK`、`FSCTL_DISMOUNT`、`SMART_SEND_DRIVE_COMMAND`、`HttpClient`、`WebClient` 命中 |

ZIP 使用 Python 3 標準函式庫的後備路徑。ReadyToRun 已在 Linux 交叉編譯時關閉。

## 既有 Windows 實測紀錄

2026-11-07 12:19–12:33（台北時間），Windows 10 Enterprise 22H2，Samsung 970 EVO Plus 1TB NVMe。完整掃描 1000.2 GB，6 分 32 秒，2000 格全部正常，平均 0.4 ms、最大 17.1 ms。

以下為修正前版本的現場實測；0.1.1 尚未在 Windows 重測。

- [ ] 啟動、系統管理員權限與 UAC 提示
- [x] 實體磁碟列舉與選取
- [ ] 容量未知時顯示「無法取得」
- [ ] SATA HDD／SSD 的 ATA SMART 讀取與健康狀態
- [x] NVMe SMART 讀取與健康狀態
- [ ] USB 外接盒／RAID 的 SMART 不可用或部分可用情境
- [x] 快速抽樣掃描與完整唯讀掃描
- [ ] 掃描停止與停止後結果呈現
- [ ] 掃描期間拔除 USB 裝置後的中止處理
- [x] HTML 報告匯出與在預設程式中開啟報告
- [ ] 序號遮罩切換與報告台北時間
- [ ] 用 NSIS 3 編譯安裝程式、安裝與解除安裝（包含捷徑、應用程式與功能資訊）
