namespace MusicBell.Core.Services;

/// <summary>释放随程序附带的浏览器扩展（B 站防登录弹窗）。</summary>
public static class BrowserExtensionExporter
{
    public const string FolderName = "MusicBell-BiliHelper";

    private const string Manifest = """
{
  "manifest_version": 3,
  "name": "MusicBell B站防登录提示",
  "version": "1.0.0",
  "description": "自动关掉 B 站的登录弹窗和遮罩，让课堂点歌不用先登录。",
  "content_scripts": [
    {
      "matches": ["*://*.bilibili.com/*", "*://bilibili.com/*"],
      "js": ["content.js"],
      "run_at": "document_idle",
      "all_frames": true
    }
  ]
}
""";

    private const string ContentJs = """
(function () {
  var SELECTORS = [
    '.bili-mini-mask',
    '.bili-mini-login',
    '.login-panel',
    '.bili-login-card',
    '.fixed-open-app-btn-wrapper',
    '.bili-mini-login-card',
    '.login-tip',
    '.bili-mini-iframe',
    '[class*="login-mask"]',
    '[class*="LoginMask"]',
    '[class*="login-panel"]'
  ];

  function clean() {
    for (var i = 0; i < SELECTORS.length; i++) {
      var nodes = document.querySelectorAll(SELECTORS[i]);
      for (var j = 0; j < nodes.length; j++) nodes[j].remove();
    }
    if (document.body && document.body.style.overflow === 'hidden') {
      document.body.style.overflow = '';
      document.documentElement.style.overflow = '';
    }
  }

  clean();
  var timer = setInterval(clean, 800);
  window.addEventListener('load', clean);
  document.addEventListener('DOMContentLoaded', clean, { capture: true });
  setTimeout(function () { clearInterval(timer); }, 120000);
  console.log('[MusicBell] B站防登录弹窗已生效');
})();
""";

    private const string Readme = """
MusicBell B站防登录提示（浏览器扩展）
====================================

作用：在 bilibili.com 上自动关掉登录弹窗和遮罩，课堂点歌不用先登录账号。

安装（Microsoft Edge / Google Chrome 通用）
1. 打开浏览器地址栏输入：
   Edge:   edge://extensions
   Chrome: chrome://extensions
2. 打开右上角（Edge 在左下角）的「开发人员模式」。
3. 点「加载解压缩的扩展」，选择本文件夹（含 manifest.json 的这一层）。
4. 装好后打开任意 B 站视频页，扩展会自动工作。

不想用了：在扩展页点「删除」即可。
""";

    /// <summary>把扩展释放到 targetDir\MusicBell-BiliHelper，并打一个 zip。返回目录与 zip 路径。</summary>
    public static (bool Success, string Message, string? Folder, string? Package) Export(string targetDir)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(targetDir)) targetDir = Path.Combine(AppPaths.AppDir, "browser-ext");
            var folder = Path.Combine(targetDir, FolderName);
            Directory.CreateDirectory(folder);

            File.WriteAllText(Path.Combine(folder, "manifest.json"), Manifest);
            File.WriteAllText(Path.Combine(folder, "content.js"), ContentJs);
            File.WriteAllText(Path.Combine(folder, "README.txt"), Readme);

            var package = folder + ".zip";
            try
            {
                if (File.Exists(package)) File.Delete(package);
                System.IO.Compression.ZipFile.CreateFromDirectory(folder, package);
            }
            catch (Exception ex)
            {
                AppLog.Warn("打包浏览器扩展失败：" + ex.Message);
                package = null!;
            }

            var msg = package is null ? folder : folder + "（另有同名的 .zip）";
            AppLog.Info("已释放浏览器扩展：" + folder);
            return (true, msg, folder, package);
        }
        catch (Exception ex)
        {
            AppLog.Error("释放浏览器扩展失败", ex);
            return (false, ex.Message, null, null);
        }
    }
}
