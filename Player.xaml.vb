Imports LibVLCSharp.Shared
Imports System.IO
Imports System.Linq
Imports System.Threading.Tasks
Imports System.Windows
Imports System.Windows.Threading

Class Player
    Private Const DefaultChannelsFolder As String = "J:\FandangoMovies\Movies\TVGUIDE\Channels"

    Private _libVLC As LibVLC
    Private _vlcPlayer As MediaPlayer
    Private _currentMedia As Media
    Private _scrubTimer As DispatcherTimer
    Private _isScrubbing As Boolean

    Public Sub New()
        InitializeComponent()

        Core.Initialize()
        _libVLC = New LibVLC()
        _vlcPlayer = New MediaPlayer(_libVLC)
        VideoView.MediaPlayer = _vlcPlayer

        AddHandler BtnPlay.Click, AddressOf BtnPlay_Click
        AddHandler BtnPause.Click, AddressOf BtnPause_Click
        AddHandler BtnStop.Click, AddressOf BtnStop_Click
        AddHandler BtnMaximize.Click, AddressOf BtnMaximize_Click
        AddHandler BtnSubtitles.Click, AddressOf BtnSubtitles_Click
        AddHandler BtnMute.Click, AddressOf BtnMute_Click
        AddHandler BtnHome.Click, AddressOf BtnHome_Click
        AddHandler Me.Closed, AddressOf Player_Closed

        _scrubTimer = New DispatcherTimer With {.Interval = TimeSpan.FromMilliseconds(500)}
        AddHandler _scrubTimer.Tick, AddressOf ScrubTimerTick
        _scrubTimer.Start()
    End Sub

    Public Sub PlayFile(path As String, Optional offset As TimeSpan = Nothing)
        Try
            If Not File.Exists(path) Then
                MessageBox.Show("File not found: " & path)
                Return
            End If

            If _currentMedia IsNot Nothing Then
                _currentMedia.Dispose()
                _currentMedia = Nothing
            End If

            _currentMedia = New Media(_libVLC, path, FromType.FromPath)
            _vlcPlayer.Play(_currentMedia)
            _vlcPlayer.Volume = CInt(VolumeSlider.Value)
            _vlcPlayer.Mute = False
            ' Force the source's real aspect ratio (never stretched to fill the
            ' window) and let libVLC auto-scale the picture to fill the VideoView
            ' as large as it can while keeping that ratio (letterboxed if needed).
            _vlcPlayer.AspectRatio = Nothing
            _vlcPlayer.Scale = 0
            BtnSubtitles.Content = "CC: Off"
            BtnMute.Content = "Mute"
            ScrubSlider.Value = 0
            TimeCurrentLabel.Text = "0:00"
            TimeTotalLabel.Text = "0:00"
            Me.Title = IO.Path.GetFileNameWithoutExtension(path)

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

    ''' Scans the channel library and starts playing whatever program is airing
    ''' right now, seeked to the correct position - used when the Player is opened
    ''' directly (e.g. from the splash screen's PLAYER button) with no specific
    ''' file already chosen for it.
    Public Async Sub PlayCurrentTimeSlot()
        Try
            Me.Title = "Loading..."
            Dim settings = UserSettings.Load()
            Dim root = GetAutoDetectedRootFolder()
            If root Is Nothing Then
                root = If(Not String.IsNullOrWhiteSpace(settings.LastRootFolder) AndAlso Directory.Exists(settings.LastRootFolder), settings.LastRootFolder, DefaultChannelsFolder)
            End If
            If Not Directory.Exists(root) Then
                MessageBox.Show("No channel library found. Pick a folder in the main guide first.")
                Return
            End If

            Dim channels = Await TVMoviesChannelGuide.FolderScanner.ScanAsync(root, Me.Dispatcher, _libVLC, Nothing, Nothing)
            If channels Is Nothing OrElse channels.Count = 0 Then
                MessageBox.Show("No channels found in " & root)
                Return
            End If

            Dim now = DateTime.Now
            Dim active As ProgramItem = Nothing
            For Each ch In channels
                active = ch.Programs.FirstOrDefault(Function(p) p.Start <= now AndAlso p.EndTime > now)
                If active IsNot Nothing Then Exit For
            Next

            If active Is Nothing OrElse String.IsNullOrWhiteSpace(active.FilePath) OrElse Not File.Exists(active.FilePath) Then
                MessageBox.Show("Nothing scheduled to play right now.")
                Return
            End If

            Dim offset = now - active.Start
            If offset < TimeSpan.Zero Then offset = TimeSpan.Zero
            PlayFile(active.FilePath, offset)
        Catch ex As Exception
            MessageBox.Show("Playback error: " & ex.Message)
        End Try
    End Sub

    Private Sub BtnPlay_Click(sender As Object, e As RoutedEventArgs)
        Try
            If _vlcPlayer IsNot Nothing Then _vlcPlayer.Play()
        Catch
        End Try
    End Sub

    Private Sub BtnPause_Click(sender As Object, e As RoutedEventArgs)
        Try
            If _vlcPlayer IsNot Nothing Then _vlcPlayer.Pause()
        Catch
        End Try
    End Sub

    Private Sub BtnStop_Click(sender As Object, e As RoutedEventArgs)
        Try
            _vlcPlayer?.Stop()
            If _currentMedia IsNot Nothing Then
                _currentMedia.Dispose()
                _currentMedia = Nothing
            End If
            ScrubSlider.Value = 0
            TimeCurrentLabel.Text = "0:00"
        Catch
        End Try
    End Sub

    Private Sub BtnMaximize_Click(sender As Object, e As RoutedEventArgs)
        Try
            Me.WindowState = If(Me.WindowState = WindowState.Maximized, WindowState.Normal, WindowState.Maximized)
        Catch
        End Try
    End Sub

    Private Sub BtnMute_Click(sender As Object, e As RoutedEventArgs)
        Try
            If _vlcPlayer Is Nothing Then Return
            _vlcPlayer.Mute = Not _vlcPlayer.Mute
            BtnMute.Content = If(_vlcPlayer.Mute, "Unmute", "Mute")
        Catch
        End Try
    End Sub

    Private Sub BtnHome_Click(sender As Object, e As RoutedEventArgs)
        Try
            Dim splash = New SplashWindow()
            CloseDuplicateWindows(splash)
            splash.Show()
        Catch
        End Try
        Me.Close()
    End Sub

    Private Sub ScrubTimerTick(sender As Object, e As EventArgs)
        Try
            If _isScrubbing OrElse _vlcPlayer Is Nothing Then Return
            Dim length = _vlcPlayer.Length
            If length <= 0 Then Return

            ScrubSlider.Value = (_vlcPlayer.Time / CDbl(length)) * 1000
            TimeCurrentLabel.Text = FormatPlayerTime(_vlcPlayer.Time)
            TimeTotalLabel.Text = FormatPlayerTime(length)
        Catch
        End Try
    End Sub

    Private Function FormatPlayerTime(ms As Long) As String
        Dim t = TimeSpan.FromMilliseconds(Math.Max(0, ms))
        If t.TotalHours >= 1 Then Return t.ToString("h\:mm\:ss")
        Return t.ToString("m\:ss")
    End Function

    Private Sub ScrubSlider_PreviewMouseDown(sender As Object, e As Input.MouseButtonEventArgs)
        _isScrubbing = True
    End Sub

    Private Sub ScrubSlider_PreviewMouseUp(sender As Object, e As Input.MouseButtonEventArgs)
        Try
            If _vlcPlayer IsNot Nothing AndAlso _vlcPlayer.Length > 0 Then
                _vlcPlayer.Time = CLng((ScrubSlider.Value / 1000) * _vlcPlayer.Length)
            End If
        Catch
        End Try
        _isScrubbing = False
    End Sub

    Private Sub ScrubSlider_ValueChanged(sender As Object, e As RoutedPropertyChangedEventArgs(Of Double))
        If _isScrubbing Then TimeCurrentLabel.Text = FormatPlayerTime(CLng((e.NewValue / 1000) * If(_vlcPlayer?.Length, 0)))
    End Sub

    Private Sub VolumeSlider_ValueChanged(sender As Object, e As RoutedPropertyChangedEventArgs(Of Double))
        Try
            Dim vol = CInt(e.NewValue)
            If _vlcPlayer IsNot Nothing Then _vlcPlayer.Volume = vol
            If VolumeLabel IsNot Nothing Then VolumeLabel.Text = vol & "%"
        Catch
        End Try
    End Sub

    Private Sub BtnSubtitles_Click(sender As Object, e As RoutedEventArgs)
        Try
            If _vlcPlayer Is Nothing Then Return

            Dim tracks = _vlcPlayer.SpuDescription
            If tracks Is Nothing OrElse tracks.Length <= 1 Then
                MessageBox.Show("No subtitle tracks found for what's currently playing.")
                Return
            End If

            Dim ids = tracks.Select(Function(t) t.Id).ToArray()
            Dim idx = Array.IndexOf(ids, _vlcPlayer.Spu)
            idx = (idx + 1) Mod ids.Length
            _vlcPlayer.SetSpu(ids(idx))

            Dim chosen = tracks(idx)
            BtnSubtitles.Content = If(chosen.Id = -1, "CC: Off", "CC: " & chosen.Name)
        Catch ex As Exception
            MessageBox.Show("Subtitle error: " & ex.Message)
        End Try
    End Sub

    Private Sub Player_Closed(sender As Object, e As EventArgs)
        Try
            _scrubTimer?.Stop()
            _vlcPlayer?.Stop()
            _currentMedia?.Dispose()
            _vlcPlayer?.Dispose()
            _libVLC?.Dispose()
        Catch
        End Try
    End Sub
End Class
