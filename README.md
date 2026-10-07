# 影音轉換大師 v1.5.4

Avalonia 桌面應用：將 **YouTube** 或 **Bilibili** 影片網址轉換成 **MP4 / MP3**（本機使用 `yt-dlp` + `ffmpeg`）。

介面依「影音轉換大師」設計稿實作：單頁式版面，網址解析、格式選擇、下載清單與即時進度，其餘功能收在可折疊區塊。

## 功能

- 貼上 YouTube / Bilibili 網址（支援多行批量）
- **搜尋影片**：關鍵字搜尋 YouTube / Bilibili（或兩者），點選結果即可解析預覽或開始轉換
- **最近搜尋紀錄**：記住最近 12 筆關鍵字（含平台），可一鍵重搜、右鍵移除或全部清除
- 解析網址：顯示標題、時長、觀看次數、上傳日期
- 輸出格式：MP4（480P / 720P / 1080P / 4K）或 MP3
- 下載清單：進度、速度、完成 / 失敗狀態
- **字幕搭配**（預設關閉）：可選下載中文優先字幕為外掛 `.srt`；MP4 並內嵌字幕軌；MP3 另產生 `.lrc` 歌詞檔
- 折疊式區塊（預設收合）：搜尋影片、下載中、已完成、檔案管理、歷史記錄、我的最愛

## 執行

```bash
dotnet run
```

## 下載（GitHub Releases）

| 檔案 | 平台 | 說明 |
|------|------|------|
| `…-win-x64.exe` | Windows x64 | 單一執行檔，下載後直接執行 |
| `…-osx-arm64.dmg` | macOS Apple Silicon | 開啟後把 App 拖進「應用程式」 |
| `…-osx-x64.dmg` | macOS Intel | 同上 |
| `…-linux-x64` | Linux x64 | 單一執行檔（`chmod +x` 後執行） |
| `…-osx-arm64.zip` / `…-osx-x64.zip` | macOS | `.app` 壓縮檔，解壓後拖進「應用程式」 |
| `…-win-x64.zip` / `….tar.gz` | Windows / 各平台 | 資料夾版，內容與上面相同 |

macOS 版未經 Apple 公證，第一次開啟若被擋，請在 App 上按右鍵 →「打開」。

建置全部發佈檔：`./build-release.sh`（輸出到 `dist/`）。

## 安裝配套工具（yt-dlp + ffmpeg）

程式啟動時若偵測到缺少工具，首頁會顯示「開始使用前：安裝配套工具」卡片：

- **一鍵安裝**：macOS 會開啟「終端機」自動安裝（沒有 Homebrew 時會先安裝 Homebrew）；Windows 會以系統管理員身分開啟 PowerShell 執行 winget。
- **複製指令**：想自己操作時，複製後貼到終端機執行。
- **重新檢查**：安裝完成後回到程式即會自動偵測，不需重啟。

macOS 手動安裝：

```bash
brew install yt-dlp ffmpeg
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

## 發佈 Windows / macOS / Linux

```bash
./build-release.sh
```

輸出在 `dist/`：

| 檔案 | 平台 |
|------|------|
| `YoutubeOrBilibiliMP3Converter-v*-win-x64.zip` | Windows x64 |
| `YoutubeOrBilibiliMP3Converter-v*-osx-arm64.tar.gz` | macOS Apple Silicon |
| `YoutubeOrBilibiliMP3Converter-v*-osx-x64.tar.gz` | macOS Intel |
| `YoutubeOrBilibiliMP3Converter-v*-linux-x64.tar.gz` | Linux x64 |

## macOS 設定

```bash
brew install yt-dlp ffmpeg
```

YouTube 若出現 `HTTP Error 403: Forbidden`，多半是 `yt-dlp` 過舊。請先更新：

```bash
brew upgrade yt-dlp
```

會員或需登入的影片，請匯出 `cookies.txt` 後在首頁「Cookies 檔案」匯入。

## 使用方式

1. 貼上影片網址（或按「貼上」從剪貼簿匯入）  
   也可展開「搜尋影片」區塊以關鍵字搜尋 YouTube / Bilibili，再點「使用網址」「解析預覽」或「開始轉換」
2. 按「解析網址」預覽影片資訊（可選）
3. 選擇 MP4 或 MP3，MP4 可選畫質
4. 確認儲存位置
5. 按「開始轉換」

預設輸出資料夾：`%USERPROFILE%\Videos\Converted`

### 搜尋說明

- **YouTube**：透過本機 `yt-dlp`（`ytsearchN:關鍵字`）
- **Bilibili**：優先使用 B 站官方搜尋 API；失敗時再嘗試 `yt-dlp bilisearch`
- 可選擇平台（YouTube、Bilibili、兩者）與每平台結果數
- 搜尋過的關鍵字會出現在「最近搜尋紀錄」：左鍵重搜、右鍵刪除單筆、可清除全部；紀錄會寫入本機設定
- 結果會過濾：僅保留**標題或介紹**含完整關鍵字的影片（避免只沾邊、不相關的推薦）

## 注意事項

YouTube 影片 / 播放清單、Bilibili 影片會由 `yt-dlp` 處理。Bilibili 會自動使用瀏覽器 headers，並在可用時讀取 Firefox / Chrome / Edge cookies。部分私人、地區限制、會員或需登入的影片仍可能失敗。

範例 Bilibili 網址：

```text
https://www.bilibili.com/video/BV158dfBAEbH/
https://www.bilibili.com/video/BV15hdfBaECr/
https://www.bilibili.com/video/BV1q4dfBNE8X/
```

## App 圖示

圖示以向量繪製，原始碼在 `tools/IconGen`。修改後重新產生 `Assets/AppIcon.icns`、`Assets/app-icon.png`、`Assets/app.ico`：

```bash
cd tools/IconGen && dotnet run -- out full && iconutil -c icns out/AppIcon.iconset -o ../../Assets/AppIcon.icns && cp out/app-icon.png out/app.ico ../../Assets/
```

舊版貓咪圖示可從 v1.3.0 取回（`git show v1.3.0:Assets/app-icon.png`）。
