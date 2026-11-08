namespace MiniAppBox;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // 仓库自检模式：MiniAppBox --selftest
        if (args.Contains("--selftest"))
        {
            SelfTest.Run();
            return 0;
        }

        // 命令行导入：MiniAppBox --import <zip 路径>（不开窗口，便于脚本/批量安装）
        if (args.Length >= 2 && args[0] == "--import")
        {
            try
            {
                var app = AppRepo.Import(args[1], Path.GetFileName(args[1]));
                Console.WriteLine($"已导入：{app.Name} ({app.Id})");
                return 0;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("导入失败：" + e.Message);
                return 1;
            }
        }

        // 命令行链接导入：MiniAppBox --import-url <github/gitee/zip 直链>
        if (args.Length >= 2 && args[0] == "--import-url")
        {
            try
            {
                var repo = UrlImporter.Parse(args[1]) ?? throw new Exception("无法识别的链接（支持 github.com/用户/仓库、gitee.com/用户/仓库、.zip 直链）");
                Console.WriteLine($"识别仓库：{repo.User}/{repo.Repo}，解析分支…");
                string zipPath = Path.Combine(Path.GetTempPath(), "miniappbox_url_" + Guid.NewGuid().ToString("N") + ".zip");
                try
                {
                    var downloaded = UrlImporter.DownloadAndFindRootsAsync(repo, zipPath).GetAwaiter().GetResult();
                    foreach (string root in downloaded.Roots)
                    {
                        var app = AppRepo.ImportDirectory(root, downloaded.RepoName);
                        Console.WriteLine($"已导入：{app.Name} ({app.Id})");
                    }
                }
                finally { try { File.Delete(zipPath); } catch { } }
                return 0;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("导入失败：" + e.Message);
                return 1;
            }
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new HomeForm());
        return 0;
    }
}
