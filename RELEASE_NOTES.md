# NexLoad Pro v1.0.0 — Initial Official Release

Welcome to the initial official release of **NexLoad Pro** (v1.0.0) — the high-speed download manager, cloud hoster stream extractor, BitTorrent client, and universal media grabber for Windows.

---

### 🚀 What's New in v1.0.0

#### 1. Multi-Threaded Download Acceleration
* Up to **32 simultaneous parallel connections** per file.
* Dynamic segmented chunk downloading with on-the-fly merging.
* Full pause and resume capability for interrupted transfers.

#### 2. Cloud & File Hosters Resolver (JDownloader Style)
* **Pixeldrain:** Direct resolution of `pixeldrain.com/u/{id}` and album lists (`/l/{id}`) into multi-threaded direct streams.
* **MEGA (mega.nz):** On-the-fly `AES-128-CTR` streaming decryption for file and folder links (`/file/` and `/folder/`).
* **MediaFire:** Landing page bypass with direct high-speed CDN extraction.
* **Google Drive:** Direct link conversion with automatic bypass of the large file virus scan warning page.
* **Dropbox & GoFile:** Instant direct stream extraction (`dl=1` & API resolution).

#### 3. Universal Media & Video Grabber
* Embedded **yt-dlp & FFmpeg** engine for **YouTube, Facebook, TikTok, Instagram, Twitter/X, Reddit**, and 1000+ sites.
* Choose from **4K UHD (2160p), 1440p 2K, 1080p FHD, 720p HD**, or **Lossless MP3 Audio Extraction**.

#### 4. Full BitTorrent & Magnet Integration
* Embedded **MonoTorrent 3.0.2** with DHT and peer-exchange acceleration.
* Selective file download dialog for multi-file torrents.

#### 5. Batch Link Grabber
* Paste any block of text, email, or chat log (`Ctrl+B`) to automatically extract, deduplicate, filter, and queue links with live cloud metadata resolution.

#### 6. 1-Click Browser Extension
* Dedicated Chromium browser extension (Chrome, Edge, Brave, Opera, Vivaldi).
* Auto-captures browser downloads and adds right-click *"Download with NexLoad Pro"* context menus.

#### 7. System Tray & Silent Background Mode (IDM Style)
* Close-to-Tray (`CloseToTray = true`) keeps the app active in the background.
* Windows auto-startup with `--background` switch.
* Full Tray context menu with quick actions.

#### 8. Native Windows Desktop UX
* Crisp Light Mode and Dark Mode themes.
* Bilingual language switching: **English** and **Sinhala (සිංහල)**.
* Integrated Bandwidth **Speed Limiter** and **Download Scheduler**.

---

### 📦 Assets Included
* `NexLoad-v1.0.0-windows-x64.zip` — Ready-to-run portable distribution package for Windows (64-bit).

---

### 💻 System Requirements
* **OS:** Windows 10 / 11 (64-bit)
* **Runtime:** [.NET 10.0 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
