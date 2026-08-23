Imports LibVLCSharp.Shared
Imports System.Diagnostics
Imports System.IO
Imports System.Linq
Imports System.Threading.Tasks
Imports System.Windows.Media
Imports System.Windows.Threading

Public Class TV_Guide
    Private _libVLC As LibVLC
    Private _vlcPlayer As LibVLCSharp.Shared.MediaPlayer
    Private _currentMedia As Media

    Private _clockTimer As DispatcherTimer
    Private _rotateTimer As DispatcherTimer

    Private _channels As List(Of Channel)
    Private _previewIndex As Integer = 0

    Private Const DefaultChannelsFolder As String = "J:\FandangoMovies\Movies\TVGUIDE\Channels"

    ' Whatever is currently showing in the small preview window - used by "Play in VLC".
    Private _currentChannel As Channel
    Private _currentProgram As ProgramItem

    Public Sub New()
        InitializeComponent()

        Core.Initialize()
        _libVLC = New LibVLC()
        _vlcPlayer = New LibVLCSharp.Shared.MediaPlayer(_libVLC)
        PreviewVideo.MediaPlayer = _vlcPlayer

        AddHandler BtnCloseGuide.Click, Sub() Me.Close()
        AddHandler BtnMaximize.Click, AddressOf BtnMaximize_Click
        AddHandler BtnMute.Click, AddressOf BtnMute_Click
        AddHandler BtnOpenExternalVlc.Click, AddressOf BtnOpenExternalVlc_Click
        AddHandler BtnHome.Click, AddressOf BtnHome_Click
        AddHandler Me.Loaded, AddressOf TV_Guide_Loaded
        AddHandler Me.Closed, AddressOf TV_Guide_Closed
    End Sub

    Private Sub BtnMaximize_Click(sender As Object, e As RoutedEventArgs)
        Try
            Me.WindowState = If(Me.WindowState = WindowState.Maximized, WindowState.Normal, WindowState.Maximized)
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

    Private Sub BtnMute_Click(sender As Object, e As RoutedEventArgs)
        Try
            If _vlcPlayer Is Nothing Then Return
            _vlcPlayer.Mute = Not _vlcPlayer.Mute
            BtnMute.Content = If(_vlcPlayer.Mute, "Unmute", "Mute")
        Catch
        End Try
    End Sub

    Private Sub VolumeSlider_ValueChanged(sender As Object, e As RoutedPropertyChangedEventArgs(Of Double))
        Try
            Dim vol = CInt(e.NewValue)
            If _vlcPlayer IsNot Nothing Then _vlcPlayer.Volume = vol
            If VolumeLabel IsNot Nothing Then VolumeLabel.Text = vol & "%"
        Catch
        End Try
    End Sub

    Private Sub TV_Guide_Loaded(sender As Object, e As EventArgs)
        ' Reuse whatever the main guide already scanned - no need to rescan the library.
        Dim mw = TryCast(Me.Owner, MainWindow)
        If mw IsNot Nothing Then
            _channels = If(mw.Channels, New List(Of Channel)())
            AfterChannelsReady()
            Return
        End If

        ' Opened directly (e.g. from the splash screen) with no main window to borrow
        ' scanned data from - scan the library ourselves so the guide isn't empty.
        _channels = New List(Of Channel)()
        Dim settings = UserSettings.Load()
        Dim root = GetAutoDetectedRootFolder()
        If root Is Nothing Then
            root = If(Not String.IsNullOrWhiteSpace(settings.LastRootFolder) AndAlso Directory.Exists(settings.LastRootFolder), settings.LastRootFolder, DefaultChannelsFolder)
        End If
        If Directory.Exists(root) Then
            NowShowingText.Text = "Scanning channel library..."
            Dim task = ScanOwnChannelsAsync(root)
        Else
            AfterChannelsReady()
        End If
    End Sub

    Private Async Function ScanOwnChannelsAsync(root As String) As Task
        Try
            _channels = Await TVMoviesChannelGuide.FolderScanner.ScanAsync(root, Me.Dispatcher, _libVLC, Nothing, Nothing)
        Catch
        End Try
        AfterChannelsReady()
    End Function

    Private Sub AfterChannelsReady()
        BuildListing()
        StartClock()
        StartChannelRotation()
    End Sub

    Private Sub BuildListing()
        ScrollPanel.Children.Clear()
        For Each r In BuildRows()
            ScrollPanel.Children.Add(r)
        Next
    End Sub

    Private Function BuildRows() As List(Of FrameworkElement)
        Dim result As New List(Of FrameworkElement)()
        Dim now = DateTime.Now

        If _channels Is Nothing OrElse _channels.Count = 0 Then
            result.Add(MakeRow("", "No channels loaded - pick a folder in the main guide first.", ""))
            Return result
        End If

        For Each ch In _channels
            Dim active = ch.Programs.FirstOrDefault(Function(p) p.Start <= now AndAlso p.EndTime > now)
            Dim title = If(active IsNot Nothing, active.Title, "(off air)")
            Dim timeText = If(active IsNot Nothing, $"{active.Start:h:mm tt} - {active.EndTime:h:mm tt}", "")
            result.Add(MakeRow(ch.Name, title, timeText, active IsNot Nothing))
        Next
        Return result
    End Function

    ''' isOnAirNow paints the row with the shared light-blue "now playing" bar
    ''' (with dark text on top so it stays readable) - every row here already only
    ''' ever shows a channel's currently-active program, so this simply distinguishes
    ''' channels genuinely airing something right now from ones that are off air.
    Private Function MakeRow(channelName As String, title As String, timeText As String, Optional isOnAirNow As Boolean = False) As FrameworkElement
        Dim row As New Grid With {.Margin = New Thickness(24, 10, 24, 10)}
        row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(180)})
        row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(240)})

        Dim textBrush As Brush = If(isOnAirNow, TryCast(Application.Current.TryFindResource("NowPlayingForeground"), Brush), Nothing)

        Dim tbChannel As New TextBlock With {
            .Text = channelName, .Foreground = If(isOnAirNow, textBrush, Brushes.Gold), .FontWeight = FontWeights.Bold,
            .FontSize = 22, .FontFamily = New FontFamily("Consolas"), .VerticalAlignment = VerticalAlignment.Center
        }
        Dim tbTitle As New TextBlock With {
            .Text = title, .Foreground = If(isOnAirNow, textBrush, Brushes.White), .FontSize = 22,
            .FontFamily = New FontFamily("Consolas"), .VerticalAlignment = VerticalAlignment.Center
        }
        Dim tbTime As New TextBlock With {
            .Text = timeText, .Foreground = If(isOnAirNow, textBrush, Brushes.LightSkyBlue), .FontSize = 18,
            .FontFamily = New FontFamily("Consolas"), .VerticalAlignment = VerticalAlignment.Center,
            .HorizontalAlignment = HorizontalAlignment.Right
        }

        Grid.SetColumn(tbChannel, 0)
        Grid.SetColumn(tbTitle, 1)
        Grid.SetColumn(tbTime, 2)
        row.Children.Add(tbChannel)
        row.Children.Add(tbTitle)
        row.Children.Add(tbTime)

        If isOnAirNow Then
            Dim highlight = TryCast(Application.Current.TryFindResource("NowPlayingBrush"), Brush)
            Return New Border With {.Background = highlight, .Child = row}
        End If
        Return row
    End Function

    Private Sub StartClock()
        UpdateClock()
        _clockTimer = New DispatcherTimer With {.Interval = TimeSpan.FromSeconds(1)}
        AddHandler _clockTimer.Tick, Sub()
                                          UpdateClock()
                                          ' Keeps each row's "(off air)"/highlighted state and
                                          ' current program in sync as the schedule moves on.
                                          BuildListing()
                                      End Sub
        _clockTimer.Start()
    End Sub

    Private Sub UpdateClock()
        ClockText.Text = DateTime.Now.ToString("h:mm:ss tt")
    End Sub

    Private Sub StartChannelRotation()
        RotatePreview()
        _rotateTimer = New DispatcherTimer With {.Interval = TimeSpan.FromSeconds(12)}
        AddHandler _rotateTimer.Tick, Sub() RotatePreview()
        _rotateTimer.Start()
    End Sub

    ''' Cycles the small preview window through channels, like the original guide
    ''' channel's rotating spotlight of what's currently on.
    Private Sub RotatePreview()
        Try
            If _channels Is Nothing OrElse _channels.Count = 0 Then
                NowShowingText.Text = "No channels available - pick a folder in the main guide first."
                Return
            End If

            Dim now = DateTime.Now
            Dim ch As Channel = Nothing
            Dim active As ProgramItem = Nothing
            For tries = 0 To _channels.Count - 1
                Dim candidate = _channels(_previewIndex Mod _channels.Count)
                _previewIndex += 1
                Dim candidateActive = candidate.Programs.FirstOrDefault(Function(p) p.Start <= now AndAlso p.EndTime > now)
                If candidateActive IsNot Nothing Then
                    ch = candidate
                    active = candidateActive
                    Exit For
                End If
            Next

            If active Is Nothing OrElse Not File.Exists(active.FilePath) Then
                NowShowingText.Text = "Nothing airing right now."
                Return
            End If

            _currentChannel = ch
            _currentProgram = active

            If _currentMedia IsNot Nothing Then
                _currentMedia.Dispose()
                _currentMedia = Nothing
            End If

            Dim offset = now - active.Start
            _currentMedia = New Media(_libVLC, active.FilePath, FromType.FromPath)
            _vlcPlayer.Play(_currentMedia)
            _vlcPlayer.Volume = CInt(VolumeSlider.Value)

            If offset > TimeSpan.Zero Then
                Task.Delay(250).ContinueWith(Sub()
                                                  Try
                                                      If _vlcPlayer IsNot Nothing Then
                                                          _vlcPlayer.Time = CInt(offset.TotalMilliseconds)
                                                      End If
                                                  Catch
                                                  End Try
                                              End Sub, TaskScheduler.Default)
            End If

            NowShowingText.Text = $"Now on {ch.Name}: {active.Title}"
            MarqueeText.Text = $"NOW ON {ch.Name.ToUpperInvariant()}: {active.Title.ToUpperInvariant()}"
        Catch
        End Try
    End Sub

    Private Sub BtnOpenExternalVlc_Click(sender As Object, e As RoutedEventArgs)
        Try
            If _currentProgram Is Nothing Then
                MessageBox.Show("Nothing airing right now.")
                Return
            End If

            Dim path = _currentProgram.FilePath
            If String.IsNullOrWhiteSpace(path) OrElse Not File.Exists(path) Then
                MessageBox.Show("File not found: " & path)
                Return
            End If

            ' Seek external VLC to how far into the program "now" actually is
            Dim offsetSeconds = CInt(Math.Max(0, (DateTime.Now - _currentProgram.Start).TotalSeconds))
            Dim args = $"""{path}"" --start-time={offsetSeconds}"

            Try
                Process.Start("vlc", args)
            Catch
                Dim vlcPath = IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "VideoLAN\VLC\vlc.exe")
                If Not File.Exists(vlcPath) Then
                    vlcPath = IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "VideoLAN\VLC\vlc.exe")
                End If
                If File.Exists(vlcPath) Then
                    Process.Start(vlcPath, args)
                Else
                    MessageBox.Show("VLC not found. Please install VLC or ensure 'vlc' is on PATH.")
                End If
            End Try
        Catch ex As Exception
            MessageBox.Show("Failed to open in VLC: " & ex.Message)
        End Try
    End Sub

    Private Sub TV_Guide_Closed(sender As Object, e As EventArgs)
        Try
            _clockTimer?.Stop()
            _rotateTimer?.Stop()
            _vlcPlayer?.Stop()
            _currentMedia?.Dispose()
            _vlcPlayer?.Dispose()
            _libVLC?.Dispose()
        Catch
        End Try
    End Sub
End Class
