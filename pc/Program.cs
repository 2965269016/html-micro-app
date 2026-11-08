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

        ApplicationConfiguration.Initialize();
        Application.Run(new HomeForm());
        return 0;
    }
}
