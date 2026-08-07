# 影音轉換大師 v1.0

Avalonia 桌面應用：將 **YouTube** 或 **Bilibili** 影片網址轉換成 **MP4 / MP3**（本機使用 `yt-dlp` + `ffmpeg`）。

介面依「影音轉換大師」設計稿實作：側邊導覽、網址解析、格式選擇、下載清單與即時進度。

## 功能

- 貼上 YouTube / Bilibili 網址（支援多行批量）
- **搜尋影片**：關鍵字搜尋 YouTube / Bilibili（或兩者），點選結果即可解析預覽或開始轉換
- 解析網址：顯示標題、時長、觀看次數、上傳日期
- 輸出格式：MP4（480P / 720P / 1080P / 4K）或 MP3
- 下載清單：進度、速度、完成 / 失敗狀態
- **字幕搭配**（預設關閉）：可選下載中文優先字幕為外掛 `.srt`；MP4 並內嵌字幕軌；MP3 另產生 `.lrc` 歌詞檔
- 側邊欄：首頁、搜尋影片、下載中、已完成、音樂提取、檔案管理、歷史記錄

## 執行

```bash
dotnet run
```

## Windows 使用前安裝

第一次使用前，請先安裝轉檔需要的兩個小工具。只要照下面做一次就好。

1. 按鍵盤的 `Windows` 鍵。
2. 輸入 `終端機` 或 `Terminal`。
3. 在「終端機」上按右鍵，選擇「以系統管理員身分執行」。
4. 複製下面這行指令，貼到終端機裡，然後按 Enter：

```powershell
winget install yt-dlp.yt-dlp Gyan.FFmpeg
```

5. 如果畫面問你是否同意，輸入 `Y`，再按 Enter。
6. 等安裝完成後，關掉終端機。
7. 重新開啟「影音轉換大師」，就可以開始使用。

如果 Windows 顯示找不到 `winget`，請先從 Microsoft Store 更新或安裝「應用程式安裝程式」。

## 建置 Windows EXE

```powershell
powershell -ExecutionPolicy Bypass -File .\build-windows.ps1
```

預設輸出：`publish\win-x64\YoutubeOrBilibiliMP3Converter.exe`。

Windows ARM：

```powershell
powershell -ExecutionPolicy Bypass -File .\build-windows.ps1 -Runtime win-arm64
```

## macOS 設定

```bash
brew install yt-dlp ffmpeg
```

## 使用方式

1. 貼上影片網址（或按「貼上」從剪貼簿匯入）  
   也可從側邊欄「搜尋影片」以關鍵字搜尋 YouTube / Bilibili，再點「使用網址」「解析預覽」或「開始轉換」
2. 按「解析網址」預覽影片資訊（可選）
3. 選擇 MP4 或 MP3，MP4 可選畫質
4. 確認儲存位置
5. 按「開始轉換」

預設輸出資料夾：`%USERPROFILE%\Videos\Converted`

### 搜尋說明

- **YouTube**：透過本機 `yt-dlp`（`ytsearchN:關鍵字`）
- **Bilibili**：優先使用 B 站官方搜尋 API；失敗時再嘗試 `yt-dlp bilisearch`
- 可選擇平台（YouTube、Bilibili、兩者）與每平台結果數

## 注意事項

YouTube 影片 / 播放清單、Bilibili 影片會由 `yt-dlp` 處理。Bilibili 會自動使用瀏覽器 headers，並在可用時讀取 Firefox / Chrome / Edge cookies。部分私人、地區限制、會員或需登入的影片仍可能失敗。

範例 Bilibili 網址：

```text
https://www.bilibili.com/video/BV158dfBAEbH/
https://www.bilibili.com/video/BV15hdfBaECr/
https://www.bilibili.com/video/BV1q4dfBNE8X/
```
