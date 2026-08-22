Imports Models
Imports System.Linq
Imports LibVLCSharp.Shared
Imports System.Windows.Forms
Imports System.Threading.Tasks
Imports System.IO
Imports System.Windows.Threading

Class MainWindow
    Private _channels As List(Of Channel)
    Private _libVLC As LibVLC
    Private _vlcPlayer As MediaPlayer
    Private _scheduler As DispatcherTimer
    Private _currentMedia As Media

    Public Sub New()
        InitializeComponent()

        ' Initialize LibVLC
        Core.Initialize()
        _libVLC = New LibVLC()
        _vlcPlayer = New MediaPlayer(_libVLC)
        VideoView.MediaPlayer = _vlcPlayer

        AddHandler BtnSelectFolder.Click, AddressOf BtnSelectFolder_Click
        AddHandler LstChannels.SelectionChanged, AddressOf LstChannels_SelectionChanged
        AddHandler LstPrograms.MouseDoubleClick, AddressOf LstPrograms_MouseDoubleClick
        AddHandler BtnPlay.Click, Sub() If _vlcPlayer IsNot Nothing Then _vlcPlayer.Play()
        AddHandler BtnPause.Click, Sub() If _vlcPlayer IsNot Nothing Then _vlcPlayer.Pause()
        AddHandler BtnStop.Click, Sub() If _vlcPlayer IsNot Nothing Then _vlcPlayer.Stop()

        _scheduler = New DispatcherTimer With {.Interval = TimeSpan.FromSeconds(5)}
        AddHandler _scheduler.Tick, AddressOf SchedulerTick
        _scheduler.Start()
    End Sub

    Private Async Sub BtnSelectFolder_Click(sender As Object, e As RoutedEventArgs)
        Using dlg = New FolderBrowserDialog()
            dlg.Description = "Select root folder containing channel subfolders"
            dlg.ShowNewFolderButton = False
            If dlg.ShowDialog() = Forms.DialogResult.OK Then
                TxtRootPath.Text = dlg.SelectedPath
                Await LoadFolderAsync(dlg.SelectedPath)
            End If
        End Using
    End Sub

    Private Async Function LoadFolderAsync(path As String) As Task
        Try
            BtnSelectFolder.IsEnabled = False
            TxtRootPath.Text = "Scanning..."
            _channels = Await FolderScanner.ScanAsync(path, Me.Dispatcher, _libVLC)
            LstChannels.ItemsSource = _channels
            TxtRootPath.Text = path & " (" & _channels.Count & " channels)"
        Catch ex As Exception
            MessageBox.Show("Error scanning folder: " & ex.Message)
        Finally
            BtnSelectFolder.IsEnabled = True
        End Try
    End Function

    Private Sub LstChannels_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)
        Dim ch = TryCast(LstChannels.SelectedItem, Channel)
        If ch Is Nothing Then
            LstPrograms.ItemsSource = Nothing
            Return
        End If

        LstPrograms.ItemsSource = ch.Programs
        TxtProgramDetails.Text = "(select a program)"
    End Sub

    Private Sub UpdateProgramDetails(p As ProgramItem)
        If p Is Nothing Then
            TxtProgramDetails.Text = "(select a program)"
            Return
        End If

        TxtProgramDetails.Text = $"Title: {p.Title}
Start: {p.Start:G}
End: {p.EndTime:G}
Duration: {p.Duration}
File: {p.FilePath}"
    End Sub

    Private Sub LstPrograms_MouseDoubleClick(sender As Object, e As System.Windows.Input.MouseButtonEventArgs)
        Dim p = TryCast(LstPrograms.SelectedItem, ProgramItem)
        If p Is Nothing Then Return
        PlayFile(p.FilePath, TimeSpan.Zero)
        UpdateProgramDetails(p)
    End Sub

    Private Sub PlayFile(path As String, Optional offset As TimeSpan = Nothing)
        Try
            If Not File.Exists(path) Then
                MessageBox.Show("File not found: " & path)
                Return
            End If

            ' Dispose previous media
            If _currentMedia IsNot Nothing Then
                _currentMedia.Dispose()
                _currentMedia = Nothing
            End If

            _currentMedia = New Media(_libVLC, path, FromType.FromPath)
            _vlcPlayer.Play(_currentMedia)
            If offset > TimeSpan.Zero Then
                ' Seek after play - schedule a slight delay to ensure playback started
                Task.Delay(250).ContinueWith(Sub()
                                                  Try
                                                      If _vlcPlayer IsNot Nothing Then
                                                          _vlcPlayer.Time = CInt(offset.TotalMilliseconds)
                                                      End If
                                                  Catch
                                                  End Try
                                              End Sub, TaskScheduler.Default)
            End If
        Catch ex As Exception
            MessageBox.Show("Playback error: " & ex.Message)
        End Try
    End Sub

    Private Sub SchedulerTick(sender As Object, e As EventArgs)
        Try
            Dim ch = TryCast(LstChannels.SelectedItem, Channel)
            If ch Is Nothing Then Return

            Dim now = DateTime.Now
            Dim active = ch.Programs.FirstOrDefault(Function(p) p.Start <= now AndAlso p.EndTime > now)
            If active IsNot Nothing Then
                ' If currently playing different file, play the scheduled one
                Dim needPlay = _currentMedia Is Nothing OrElse Not String.Equals(_currentMedia.Mrl, New Uri(active.FilePath).AbsoluteUri, StringComparison.OrdinalIgnoreCase)
                If needPlay Then
                    Dim offset = now - active.Start
                    PlayFile(active.FilePath, offset)
                Else
                    ' If same media but behind schedule, seek
                    Dim offset = now - active.Start
                    If offset.TotalSeconds > 5 Then
                        Try
                            _vlcPlayer.Time = CInt(offset.TotalMilliseconds)
                        Catch
                        End Try
                    End If
                End If
                UpdateProgramDetails(active)
            End If
        Catch ex As Exception
            ' ignore scheduler errors
        End Try
    End Sub

End Class
