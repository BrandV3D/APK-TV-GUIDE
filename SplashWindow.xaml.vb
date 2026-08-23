Class SplashWindow
    Public Sub New()
        InitializeComponent()
        ' controls created after InitializeComponent; use FindName to attach handlers
        Dim b1 = TryCast(FindName("BtnSplashGuide"), System.Windows.Controls.Button)
        Dim b2 = TryCast(FindName("BtnSplashChannels"), System.Windows.Controls.Button)
        Dim b3 = TryCast(FindName("BtnSplashPlayback"), System.Windows.Controls.Button)
        Dim b4 = TryCast(FindName("BtnSplashPlayer"), System.Windows.Controls.Button)
        Dim b5 = TryCast(FindName("BtnSplashLiveTv"), System.Windows.Controls.Button)
        If b1 IsNot Nothing Then AddHandler b1.Click, AddressOf BtnSplashGuide_Click
        If b2 IsNot Nothing Then AddHandler b2.Click, AddressOf BtnSplashChannels_Click
        If b3 IsNot Nothing Then AddHandler b3.Click, AddressOf BtnSplashPlayback_Click
        If b4 IsNot Nothing Then AddHandler b4.Click, AddressOf BtnSplashPlayer_Click
        If b5 IsNot Nothing Then AddHandler b5.Click, AddressOf BtnSplashLiveTv_Click
    End Sub

    Private Sub BtnSplashGuide_Click(sender As Object, e As RoutedEventArgs)
        Try
            Dim w = New MainWindow()
            CloseDuplicateWindows(w)
            w.Show()
        Catch
        End Try
        Me.Close()
    End Sub

    Private Sub BtnSplashChannels_Click(sender As Object, e As RoutedEventArgs)
        Try
            Dim w = New Window1()
            CloseDuplicateWindows(w)
            w.Show()
        Catch
        End Try
        Me.Close()
    End Sub

    Private Sub BtnSplashPlayback_Click(sender As Object, e As RoutedEventArgs)
        Try
            Dim w = New TV_Guide()
            CloseDuplicateWindows(w)
            w.Show()
        Catch
        End Try
        Me.Close()
    End Sub

    Private Sub BtnSplashPlayer_Click(sender As Object, e As RoutedEventArgs)
        Try
            Dim w = New Player()
            CloseDuplicateWindows(w)
            w.Show()
            w.PlayCurrentTimeSlot()
        Catch
        End Try
        Me.Close()
    End Sub

    Private Sub BtnSplashLiveTv_Click(sender As Object, e As RoutedEventArgs)
        Try
            Dim w = New LiveTV()
            CloseDuplicateWindows(w)
            w.Show()
        Catch
        End Try
        Me.Close()
    End Sub
End Class
