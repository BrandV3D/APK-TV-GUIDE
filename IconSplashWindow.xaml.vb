Imports System.IO
Imports System.Windows.Threading

Class IconSplashWindow
    Private _timer As DispatcherTimer
    Private _navigated As Boolean

    Public Sub New()
        InitializeComponent()

        Try
            Dim iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Icon_Guide.ico")
            If File.Exists(iconPath) Then
                IconImage.Source = New BitmapImage(New Uri(iconPath))
            End If
        Catch
        End Try

        _timer = New DispatcherTimer With {.Interval = TimeSpan.FromSeconds(2.5)}
        AddHandler _timer.Tick, AddressOf Timer_Tick
        _timer.Start()
    End Sub

    Private Sub Timer_Tick(sender As Object, e As EventArgs)
        GoToSplash()
    End Sub

    Private Sub Grid_MouseLeftButtonUp(sender As Object, e As Input.MouseButtonEventArgs)
        GoToSplash()
    End Sub

    Private Sub GoToSplash()
        If _navigated Then Return
        _navigated = True

        Try
            _timer?.Stop()
        Catch
        End Try

        Try
            Dim splash = New SplashWindow()
            CloseDuplicateWindows(splash)
            splash.Show()
        Catch
        End Try
        Me.Close()
    End Sub
End Class
