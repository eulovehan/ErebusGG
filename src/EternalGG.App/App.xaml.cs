using System.Windows;

namespace EternalGG.App;

/** 앱 시작과 예외 안내 */
public partial class App : Application
{
    /** 미처리 화면 예외의 안내 및 앱 시작 */
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show("처리 중 오류가 발생했습니다. 앱을 다시 실행해 주세요.", "Erebus GG");
            args.Handled = true;
        };
        base.OnStartup(e);
    }
}
