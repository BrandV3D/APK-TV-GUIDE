Imports System.IO
Imports System.Windows.Threading

Class Application

    Private Sub Application_Startup(sender As Object, e As StartupEventArgs) Handles Me.Startup
        AddHandler DispatcherUnhandledException, AddressOf Application_DispatcherUnhandledException
        AddHandler AppDomain.CurrentDomain.UnhandledException, AddressOf CurrentDomain_UnhandledException
    End Sub

    Private Sub Application_DispatcherUnhandledException(sender As Object, e As DispatcherUnhandledExceptionEventArgs)
        LogCrash(e.Exception)
        e.Handled = True
    End Sub

    Private Sub CurrentDomain_UnhandledException(sender As Object, e As UnhandledExceptionEventArgs)
        LogCrash(TryCast(e.ExceptionObject, Exception))
    End Sub

    Private Sub LogCrash(ex As Exception)
        Try
            Dim logPath = Path.Combine(Path.GetTempPath(), "tvguide_crash.txt")
            File.AppendAllText(logPath, $"{DateTime.Now:O}{Environment.NewLine}{ex?.ToString()}{Environment.NewLine}{Environment.NewLine}")
        Catch
        End Try
    End Sub

End Class
