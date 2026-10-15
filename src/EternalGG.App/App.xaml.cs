using System.Windows;

namespace EternalGG.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show("처리 중 오류가 발생했습니다. 앱을 다시 실행해 주세요.\n" + args.Exception.Message, "EternalGG");
            args.Handled = true;
        };
        base.OnStartup(e);
    }
}
