Imports System.Windows.Threading
Imports System.IO
Imports System.Windows.Forms

Class SplashWindow
    Public Sub New()
        InitializeComponent()
        ' controls created after InitializeComponent; use FindName to attach handlers
        Dim b1 = TryCast(FindName("BtnSplashGuide"), System.Windows.Controls.Button)
        Dim b2 = TryCast(FindName("BtnSplashChannels"), System.Windows.Controls.Button)
        Dim b3 = TryCast(FindName("BtnSplashPlayback"), System.Windows.Controls.Button)
        If b1 IsNot Nothing Then AddHandler b1.Click, AddressOf BtnSplashGuide_Click
        If b2 IsNot Nothing Then AddHandler b2.Click, AddressOf BtnSplashChannels_Click
        If b3 IsNot Nothing Then AddHandler b3.Click, AddressOf BtnSplashPlayback_Click
    End Sub

    Private Sub BtnSplashGuide_Click(sender As Object, e As RoutedEventArgs)
        Try
            Dim w = New MainWindow()
            w.Show()
        Catch
        End Try
        Me.Close()
    End Sub

    Private Sub BtnSplashChannels_Click(sender As Object, e As RoutedEventArgs)
        Try
            ' open channel editor using last saved root or prompt
            Dim settings = UserSettings.Load()
            Dim root = settings.LastRootFolder
            If String.IsNullOrWhiteSpace(root) OrElse Not Directory.Exists(root) Then
                Using dlg = New System.Windows.Forms.FolderBrowserDialog()
                    dlg.Description = "Select root folder for channels"
                    If dlg.ShowDialog() = System.Windows.Forms.DialogResult.OK Then root = dlg.SelectedPath
                End Using
            End If
            If Not String.IsNullOrWhiteSpace(root) AndAlso Directory.Exists(root) Then
                Dim ed = New ChannelEditor(root)
                ed.Owner = Nothing
                ed.ShowDialog()
            End If
        Catch
        End Try
        Me.Close()
    End Sub

    Private Sub BtnSplashPlayback_Click(sender As Object, e As RoutedEventArgs)
        Try
            Dim w = New MainWindow()
            w.Show()
        Catch
        End Try
        Me.Close()
    End Sub
End Class
