# 播放清單與頻道解析可行性評估

測試日期：2026-10-31（Asia/Taipei）。本機工具：yt-dlp 2026.10.21。檢查的專案版本：v1.5.7，提交 `e646036`。

本文件記錄網址解析實測與後續實作建議。以下測試只讀取清單或影片中繼資料，沒有下載影音；結果不代表所有影片均可播放或下載。本次提交沒有新增列表介面或修改下載行為。

## 實測結果

| 類型 | 測試對象 | 結果與驗證範圍 |
| --- | --- | --- |
| YouTube 播放清單 | [影片分類：全球經濟](https://www.youtube.com/playlist?list=PL4fu-px0zajPUnwuUsn6Fv5mTdW3fCVIW) | 成功取得 286 筆；回傳的 `playlist_count` 也是 286。 |
| YouTube 頻道影片頁 | [君語瀾 JunYuLan](https://www.youtube.com/@junyulan/videos) | 成功取得 93 筆；其中 2 筆的 `availability` 為 `subscriber_only`，其餘 91 筆此欄位為空，不能據此保證沒有存取限制。 |
| Bilibili 合集 | [合集·黑鸦AI日报](https://space.bilibili.com/3706929260006322/lists/7569444?type=season) | 成功取得 248 筆 BV ID 與影片網址。 |
| Bilibili 單片 | [BV1PxPTzWEMX](https://www.bilibili.com/video/BV1PxPTzWEMX/) | 回傳單支影片，標題為「Claude 被曝参与美军空袭伊朗；Qwen 3.5 系列小模型将发」；沒有自動展開所屬合集。 |
| Bilibili 頻道投稿 | [星话大白，UID 2233213](https://space.bilibili.com/2233213/upload/video) | 限制前 30 筆時成功取得列表。再次以 60 筆上限測試時遭 HTTP 412 拒絕；未驗證第二頁或完整頻道。 |

YouTube 頻道回傳的頻道 ID 為 `UCLlUDIbSgNiX0bL_2DRXBQw`，訂閱數為 30,700。截圖顯示 95 部影片，但 `/videos` 實測回傳 93 筆；差異原因尚未驗證，不將 93 筆視為包含 Shorts 等分頁的頻道總影片數。

君語瀾頻道此次回傳的兩筆會員標記影片 ID 為 `wDi_qWLDHcE`、`ZUFjsvL6560`。本次未驗證會員內容下載；後續下載仍須由具備相應資格的登入帳號取得存取權。

Bilibili 頻道第一筆影片為 `BV1Ddxr6KETe`，另行讀取此影片中繼資料確認 UP 主為「星话大白」、UID 為 `2233213`。HTTP 412 發生於後續擴大測試，不能判定是第二頁本身失敗。

## 可重現的解析指令

下列指令忽略使用者 yt-dlp 設定，僅取得 JSON。不同時間、網路與登入狀態可能得到不同數量或風控結果。

```powershell
yt-dlp --ignore-config --flat-playlist --dump-single-json --skip-download --socket-timeout 15 --retries 1 --extractor-retries 1 --encoding utf-8 'https://www.youtube.com/playlist?list=PL4fu-px0zajPUnwuUsn6Fv5mTdW3fCVIW'

yt-dlp --ignore-config --flat-playlist --dump-single-json --skip-download --socket-timeout 15 --retries 1 --extractor-retries 1 --encoding utf-8 'https://www.youtube.com/@junyulan/videos'

yt-dlp --ignore-config --flat-playlist --dump-single-json --skip-download --socket-timeout 15 --retries 1 --extractor-retries 1 --encoding utf-8 'https://space.bilibili.com/3706929260006322/lists/7569444?type=season'

yt-dlp --ignore-config --flat-playlist --playlist-end 30 --dump-single-json --skip-download --socket-timeout 15 --retries 1 --extractor-retries 1 --encoding utf-8 'https://space.bilibili.com/2233213/upload/video'
```

## v1.5.7 現況

- `MainWindow.cs` 的 `ParseUrlCoreAsync` 只有 YouTube 頻道專用分流；Bilibili 頻道及合集仍走 `DumpVideoInfoAsync`。
- YouTube 的 `DumpChannelInfoAsync` 先由 `FindChannelSampleVideoUrlAsync` 取得第一支影片，再讀取該影片資訊作為頻道預覽；不會列出整個頻道。
- 單片解析使用 `--dump-single-json --no-playlist --skip-download`，並按單片模型讀取結果，沒有展開 playlist `entries` 的介面。
- 下載設定已有「下載整份播放清單」與 `--yes-playlist`，但下載佇列仍按輸入網址建立項目，尚未提供逐片勾選的列表流程。
- `VideoAccess.cs` 已辨識 `subscriber_only` 會員狀態，可供後續列表標示沿用。
- Bilibili 自動 headers 與預設瀏覽器 Cookies 選擇主要以 `/video/` 網址判斷，新增 `space.bilibili.com` 支援時應一併檢視。

## 建議實作範圍

1. 將輸入分為單片、播放清單／合集、頻道投稿，為列表建立獨立的結果模型及解析流程。
2. 先用 `--flat-playlist` 取得影片 ID 與網址，再按需補齊標題、封面、時長及權限資訊。此次 Bilibili 合集與頻道的 flat entries 主要只有 ID、網址；缺少欄位應顯示待載入狀態。
3. 提供「解析列表 → 勾選影片 → 加入下載佇列」，並按平台與影片 ID 處理重複項目。
4. 對大量列表提供取消、分批載入、快取及風控退避重試。遇到 HTTP 412 時保留已取得結果，避免立即反覆請求。
5. 將會員、私人、已刪除及登入限制呈現為逐片狀態，讓單筆失敗不影響其他影片。
6. Bilibili 單片反查所屬合集另列為待驗證功能；本次直接讀取影片資訊 API 曾收到 HTTP 412，尚未確認可靠的自動反查流程。

## 參考

- [yt-dlp 使用說明](https://github.com/yt-dlp/yt-dlp#usage-and-options)：`--flat-playlist` 可能省略部分影片資訊。
- [yt-dlp Bilibili extractor](https://github.com/yt-dlp/yt-dlp/blob/master/yt_dlp/extractor/bilibili.py)：包含 `BilibiliSpaceVideoIE`、`BilibiliCollectionListIE` 及分頁處理。此連結指向會持續變動的上游程式，實測工具版本以上述紀錄為準。
