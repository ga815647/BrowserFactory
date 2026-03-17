using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using WebDriverManager;
using WebDriverManager.DriverConfigs.Impl;
using WebDriverManager.Helpers;

public static class BrowserFactory
{
    /// <summary>
    /// 建立標準 ChromeDriver。
    /// </summary>
    /// <param name="headless">是否使用無頭模式，預設 false</param>
    /// <param name="downloadDir">下載目錄，null 時使用預設路徑 C:\ProgramData\ChromeDriver\Downloads</param>
    public static IWebDriver CreateChromeDriver(bool headless = false, string? downloadDir = null)
    {
        // 1. 定義標準化目錄結構 (Root: C:\ProgramData\ChromeDriver)
        string baseDir        = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ChromeDriver");
        string driverCacheDir = Path.Combine(baseDir, "Drivers");
        string downloadTarget = downloadDir ?? Path.Combine(baseDir, "Downloads");

        Directory.CreateDirectory(driverCacheDir);
        Directory.CreateDirectory(downloadTarget);

        // 2. Offline-First Strategy
        // 有本機快取的 Driver 直接使用，跳過 WDM 網路請求
        string? cachedDriverPath = TryFindCachedDriver(driverCacheDir);

        if (cachedDriverPath == null)
        {
            new DriverManager(driverCacheDir)
                .SetUpDriver(new ChromeConfig(), VersionResolveStrategy.MatchingBrowser);

            // 重新尋找剛下載的路徑
            cachedDriverPath = TryFindCachedDriver(driverCacheDir);
        }

        // 3. 設定 ChromeOptions
        var options = new ChromeOptions();

        // 下載行為
        options.AddUserProfilePreference("download.default_directory",  downloadTarget);
        options.AddUserProfilePreference("download.prompt_for_download", false);
        options.AddUserProfilePreference("download.directory_upgrade",   true);
        options.AddUserProfilePreference("safebrowsing.enabled",         true);

        // 穩定性參數
        options.AddArgument("--no-sandbox");
        options.AddArgument("--disable-dev-shm-usage");
        options.AddArgument("--disable-gpu");

        if (headless)
        {
            options.AddArgument("--headless=new"); // Chrome 112+ 推薦寫法
            options.AddArgument("--window-size=1920,1080");
        }

        // 4. 啟動 Driver（明確指定 driver 路徑）
        var service = cachedDriverPath != null
            ? ChromeDriverService.CreateDefaultService(Path.GetDirectoryName(cachedDriverPath), Path.GetFileName(cachedDriverPath))
            : ChromeDriverService.CreateDefaultService();

        service.HideCommandPromptWindow = true;

        return new ChromeDriver(service, options);
    }

    // ── 私有輔助方法 ────────────────────────────────────────────

    /// <summary>
    /// 從快取目錄尋找與當前 Chrome Major 版本相符的 chromedriver.exe
    /// </summary>
    private static string? TryFindCachedDriver(string driverCacheDir)
    {
        try
        {
            var chromePath = RegistryHelper.FindChromeExecutable();
            if (string.IsNullOrEmpty(chromePath) || !File.Exists(chromePath)) return null;

            var fileVersion = FileVersionInfo.GetVersionInfo(chromePath).FileVersion;
            if (string.IsNullOrEmpty(fileVersion)) return null;

            var major         = fileVersion.Split('.')[0];
            var chromeCacheRoot = Path.Combine(driverCacheDir, "Chrome");
            if (!Directory.Exists(chromeCacheRoot)) return null;

            // 用數字排序避免字典序問題（"9" > "130" 的陷阱）
            var bestVersion = Directory.GetDirectories(chromeCacheRoot)
                .Select(d => Path.GetFileName(d))
                .Where(d => d.StartsWith(major + "."))
                .OrderByDescending(d =>
                {
                    var parts = d.Split('.');
                    return parts.Length >= 2 && int.TryParse(parts[1], out var minor) ? minor : 0;
                })
                .FirstOrDefault();

            if (bestVersion == null) return null;

            var candidate = Path.Combine(chromeCacheRoot, bestVersion, "X64", "chromedriver.exe");
            return File.Exists(candidate) ? candidate : null;
        }
        catch
        {
            return null; // 任何錯誤都 fallback 給 WDM 處理
        }
    }

    /// <summary>
    /// 從 Windows Registry 尋找 Chrome 執行檔路徑
    /// </summary>
    private static class RegistryHelper
    {
        public static string? FindChromeExecutable()
        {
            if (!OperatingSystem.IsWindows()) return null;

            var keys = new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe"
            };

            foreach (var keyName in keys)
            {
                try
                {
                    using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(keyName)
                                 ?? Microsoft.Win32.Registry.CurrentUser.OpenSubKey(keyName);
                    var path = key?.GetValue("") as string;
                    if (!string.IsNullOrEmpty(path) && File.Exists(path)) return path;
                }
                catch { /* ignore */ }
            }

            return null;
        }
    }
}
