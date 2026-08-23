Imports Models
Imports System.Linq
Imports LibVLCSharp.Shared
Imports System.Windows.Forms
Imports System.Threading.Tasks
Imports System.IO
Imports System.Windows.Threading
Imports System.Diagnostics

Class MainWindow
    Private Const DefaultMovieFolder As String = "J:\FandangoMovies\Movies\TVGUIDE"
    ' Each subfolder here is a channel; the .mp4 files inside it are that channel's playlist.
    Private Const DefaultChannelsFolder As String = "J:\FandangoMovies\Movies\TVGUIDE\Channels"
    Private _settings As UserSettings
    Private _schedualPlayer As Player
    Private Class TimelineBlock
        Implements System.ComponentModel.INotifyPropertyChanged
        Public Event PropertyChanged As System.ComponentModel.PropertyChangedEventHandler Implements System.ComponentModel.INotifyPropertyChanged.PropertyChanged

        Public Property Title As String
        Public Property TimeText As String
        Public Property Width As Double
        Public Property ToolTip As String
        Public Property Program As ProgramItem

        Private _isCurrent As Boolean
        ''' True for the single block that's on air right now - kept live by
        ''' SchedulerTick so the highlight bar tracks the schedule without needing
        ''' to rebuild the whole Timeline list every tick.
        Public Property IsCurrent As Boolean
            Get
                Return _isCurrent
            End Get
            Set(value As Boolean)
                If _isCurrent <> value Then
                    _isCurrent = value
                    RaiseEvent PropertyChanged(Me, New System.ComponentModel.PropertyChangedEventArgs(NameOf(IsCurrent)))
                End If
            End Set
        End Property
    End Class

    Private Class TimeLabel
        Public Property Text As String
        Public Property Background As System.Windows.Media.Brush
        Public Property Width As Double
    End Class

    Private _channels As List(Of Channel)
    Private _libVLC As LibVLC
    Private _vlcPlayer As MediaPlayer
    Private _scheduler As DispatcherTimer
    Private _scrubTimer As DispatcherTimer
    Private _isScrubbing As Boolean
    Private _currentMedia As Media
    Private _rulerStart As DateTime
    Private _ruler24Mode As Boolean
    Private _rootFolderPath As String

    Public Sub New()
        InitializeComponent()

        ' Initialize LibVLC
        Core.Initialize()
        _libVLC = New LibVLC()
        _vlcPlayer = New MediaPlayer(_libVLC)
        VideoView.MediaPlayer = _vlcPlayer

        AddHandler MenuHome.Click, AddressOf MenuHome_Click
        AddHandler BtnHome.Click, AddressOf MenuHome_Click
        AddHandler MenuChannelsGrid.Click, AddressOf MenuChannelsGrid_Click
        AddHandler MenuEditChannels.Click, AddressOf MenuEditChannels_Click
        AddHandler MenuGuide.Click, AddressOf MenuGuide_Click
        AddHandler MenuLiveTv.Click, AddressOf MenuLiveTv_Click
        AddHandler LstChannels.SelectionChanged, AddressOf LstChannels_SelectionChanged
        AddHandler LstChannels.PreviewMouseWheel, AddressOf LstChannels_PreviewMouseWheel
        AddHandler LstPrograms.MouseDoubleClick, AddressOf LstPrograms_MouseDoubleClick
        AddHandler BtnPlay.Click, AddressOf BtnPlay_Click
        AddHandler BtnPause.Click, AddressOf BtnPause_Click
        AddHandler BtnStop.Click, AddressOf BtnStop_Click
        AddHandler BtnMaximizeVideo.Click, AddressOf BtnMaximize_Click
        AddHandler BtnMute.Click, AddressOf BtnMute_Click
        AddHandler BtnOpenExternalVlc.Click, AddressOf BtnOpenExternalVlc_Click
        AddHandler BtnPlayNowInPlayer.Click, AddressOf BtnPlayNowInPlayer_Click
        AddHandler BtnSubtitles.Click, AddressOf BtnSubtitles_Click
        AddHandler btn_GoTo_TV_Guide.Click, AddressOf btn_GoTo_TV_Guide_Click
        AddHandler btn_Guide.Click, AddressOf btn_Guide_Click
        AddHandler btn_LiveTv.Click, AddressOf MenuLiveTv_Click

        _scheduler = New DispatcherTimer With {.Interval = TimeSpan.FromSeconds(5)}
        AddHandler _scheduler.Tick, AddressOf SchedulerTick
        _scheduler.Start()

        _scrubTimer = New DispatcherTimer With {.Interval = TimeSpan.FromMilliseconds(500)}
        AddHandler _scrubTimer.Tick, AddressOf ScrubTimerTick
        _scrubTimer.Start()

        ' Load settings and auto-open last folder if present
        Dim autoLoadingRealFolder = False
        Try
            _settings = UserSettings.Load()
            Dim rootToLoad As String = Nothing
            Dim linkedRoot = GetAutoDetectedRootFolder()
            If linkedRoot IsNot Nothing Then
                rootToLoad = linkedRoot
            ElseIf Not String.IsNullOrWhiteSpace(_settings.LastRootFolder) AndAlso Directory.Exists(_settings.LastRootFolder) Then
                rootToLoad = _settings.LastRootFolder
            ElseIf Directory.Exists(DefaultChannelsFolder) Then
                rootToLoad = DefaultChannelsFolder
            ElseIf Directory.Exists(DefaultMovieFolder) Then
                rootToLoad = DefaultMovieFolder
            End If
            If rootToLoad IsNot Nothing Then
                _rootFolderPath = rootToLoad
                autoLoadingRealFolder = True
                ' start scan in background (do not await)
                Dim _task = StartScanAsync(rootToLoad)
            End If
        Catch
        End Try
        ' If no channels loaded and no real folder is being scanned, generate 30 demo channels
        If Not autoLoadingRealFolder AndAlso (_channels Is Nothing OrElse _channels.Count = 0) Then
            GenerateDemoChannels(30)
        End If
        ' initialize time ruler state and generate
        _ruler24Mode = False
        _rulerStart = DateTime.Now
        GenerateTimeRuler()
        ' no-op comment to ensure placement
        AddHandler Me.SizeChanged, AddressOf MainWindow_SizeChanged
    End Sub

    Public ReadOnly Property Channels As List(Of Channel)
        Get
            Return _channels
        End Get
    End Property

    Public Sub SelectChannelByIndex(idx As Integer)
        Try
            If _channels Is Nothing OrElse idx < 0 OrElse idx >= _channels.Count Then Return
            LstChannels.SelectedIndex = idx
            LstChannels.ScrollIntoView(LstChannels.SelectedItem)
        Catch
        End Try
    End Sub

    Private Sub MenuHome_Click(sender As Object, e As RoutedEventArgs)
        Try
            Dim splash = New SplashWindow()
            CloseDuplicateWindows(splash)
            splash.Show()
            Me.Close()
        Catch
        End Try
    End Sub

    Private Sub btn_GoTo_TV_Guide_Click(sender As Object, e As RoutedEventArgs)
        Try
            Dim tvGuide = New TV_Guide()
            tvGuide.Owner = Me
            CloseDuplicateWindows(tvGuide)
            tvGuide.Show()
        Catch
        End Try
    End Sub

    Private Sub btn_Guide_Click(sender As Object, e As RoutedEventArgs)
        Try
            Dim w1 = New Window1(_channels)
            w1.Owner = Me
            CloseDuplicateWindows(w1)
            w1.Show()
        Catch
        End Try
    End Sub

    Private Sub MenuChannelsGrid_Click(sender As Object, e As RoutedEventArgs)
        Try
            Dim cg = New ChannelsGrid()
            cg.Owner = Me
            CloseDuplicateWindows(cg)
            cg.Show()
        Catch
        End Try
    End Sub

    Private Sub MenuEditChannels_Click(sender As Object, e As RoutedEventArgs)
        BtnEditChannels_Click(sender, e)
    End Sub

    Private Sub MenuGuide_Click(sender As Object, e As RoutedEventArgs)
        ' Already in guide - maybe refresh
        Me.Activate()
    End Sub

    Private Sub MenuLiveTv_Click(sender As Object, e As RoutedEventArgs)
        Try
            Dim w = New LiveTV()
            w.Owner = Me
            CloseDuplicateWindows(w)
            w.Show()
        Catch
        End Try
    End Sub

    Private Sub TimelineListView_MouseDoubleClick(sender As Object, e As System.Windows.Input.MouseButtonEventArgs)
        Try
            Dim block = TryCast(TimelineListView.SelectedItem, TimelineBlock)
            If block Is Nothing OrElse block.Program Is Nothing Then Return
            Dim p = block.Program
            PlayFile(p.FilePath, TimeSpan.Zero)
            UpdateProgramDetails(p)
        Catch
        End Try
    End Sub

    Private Sub MainWindow_SizeChanged(sender As Object, e As SizeChangedEventArgs)
        Try
            Dim itemHeight = 40.0
            Dim rowHeight = RootGrid.RowDefinitions(1).ActualHeight
            If Double.IsNaN(rowHeight) OrElse rowHeight <= 0 Then Return
            Dim count = Math.Max(1, Math.Floor(rowHeight / itemHeight))
            Dim height = count * itemHeight
            LstChannels.Height = height
            ' regenerate time ruler so slot widths adjust to new center column width
            GenerateTimeRuler()
        Catch
        End Try
    End Sub

    Private Sub GenerateDemoChannels(count As Integer)
        _channels = Enumerable.Range(1, count).Select(Function(i) New Channel With {.Id = i.ToString(), .Name = $"Channel {i}", .FolderPath = String.Empty}).ToList()
        LstChannels.ItemsSource = _channels
        LstChannels.SelectedIndex = 0
    End Sub

    Private Sub LstChannels_PreviewMouseWheel(sender As Object, e As System.Windows.Input.MouseWheelEventArgs)
        Try
            If _channels Is Nothing OrElse _channels.Count = 0 Then Return
            e.Handled = True
            Dim steps = If(e.Delta > 0, -1, 1)
            Dim idx = LstChannels.SelectedIndex
            If idx < 0 Then idx = 0
            idx = ((idx + steps) Mod _channels.Count + _channels.Count) Mod _channels.Count
            LstChannels.SelectedIndex = idx
            LstChannels.ScrollIntoView(LstChannels.SelectedItem)
        Catch
        End Try
    End Sub

    Private Sub LstChannels_PreviewKeyDown(sender As Object, e As System.Windows.Input.KeyEventArgs)
        Try
            If _channels Is Nothing OrElse _channels.Count = 0 Then Return
            Dim idx = LstChannels.SelectedIndex
            If idx < 0 Then idx = 0
            If e.Key = System.Windows.Input.Key.Up Then
                idx = ((idx - 1) Mod _channels.Count + _channels.Count) Mod _channels.Count
                LstChannels.SelectedIndex = idx
                LstChannels.ScrollIntoView(LstChannels.SelectedItem)
                e.Handled = True
            ElseIf e.Key = System.Windows.Input.Key.Down Then
                idx = ((idx + 1) Mod _channels.Count + _channels.Count) Mod _channels.Count
                LstChannels.SelectedIndex = idx
                LstChannels.ScrollIntoView(LstChannels.SelectedItem)
                e.Handled = True
            End If
        Catch
        End Try
    End Sub

    Private Sub GenerateTimeRuler(center As DateTime)
        ' deprecated
    End Sub

    Private Sub GenerateTimeRuler()
        Try
            Dim center = _rulerStart
            Dim start = New DateTime(center.Year, center.Month, center.Day, center.Hour, 0, 0)
            ' round down to nearest half hour
            Dim minuteOffset = start.Minute Mod 30
            If minuteOffset <> 0 Then start = start.AddMinutes(-minuteOffset)

            Dim labels As New List(Of TimeLabel)()
            Dim slots As Integer
            If _ruler24Mode Then
                slots = 48 ' 24 hours, half-hour slots
            Else
                slots = 14 ' 7 hours => 14 half-hour slots
            End If

            ' compute available width in center column
            Dim available As Double = 400 ' fallback
            Try
                If RootGrid IsNot Nothing Then
                    available = RootGrid.ColumnDefinitions(1).ActualWidth - 40 ' padding for buttons
                    If available < 100 Then available = 100
                End If
            Catch
            End Try

            Dim slotWidth = Math.Max(80, Math.Floor(available / slots))

            For i = 0 To slots - 1
                Dim t = start.AddMinutes(i * 30)
                Dim text = t.ToString("h:mm tt")
                Dim bg = System.Windows.Media.Brushes.Transparent
                If Math.Abs((DateTime.Now - t).TotalMinutes) < 15 Then
                    bg = CType(Resources("LightHighlight"), System.Windows.Media.Brush)
                End If
                labels.Add(New TimeLabel With {.Text = text, .Background = bg, .Width = slotWidth})
            Next
            TimeRuler.ItemsSource = labels
        Catch
        End Try
    End Sub

    Private Sub TimeRulerItem_Click(sender As Object, e As System.Windows.Input.MouseButtonEventArgs)
        Try
            Dim tb = TryCast(sender, FrameworkElement)
            If tb Is Nothing Then Return
            Dim lbl = TryCast(tb.DataContext, TimeLabel)
            If lbl Is Nothing Then Return
            ' parse time text to today datetime
            Dim t As DateTime
            If DateTime.TryParse(lbl.Text, t) Then
                ' Use today's date component
                Dim now = DateTime.Now
                Dim target = New DateTime(now.Year, now.Month, now.Day, t.Hour, t.Minute, 0)
                Dim ch = TryCast(LstChannels.SelectedItem, Channel)
                If ch Is Nothing Then Return
                Dim prog = ch.Programs.FirstOrDefault(Function(p) p.Start <= target AndAlso p.EndTime > target)
                If prog IsNot Nothing Then
                    PlayFile(prog.FilePath, target - prog.Start)
                    UpdateProgramDetails(prog)
                Else
                    MessageBox.Show("No program at that time on selected channel.")
                End If
            End If
        Catch
        End Try
    End Sub

    Private Sub BtnRulerLeft_Click(sender As Object, e As RoutedEventArgs)
        Try
            _rulerStart = _rulerStart.AddMinutes(-30)
            GenerateTimeRuler()
        Catch
        End Try
    End Sub

    Private Sub BtnRulerRight_Click(sender As Object, e As RoutedEventArgs)
        Try
            _rulerStart = _rulerStart.AddMinutes(30)
            GenerateTimeRuler()
        Catch
        End Try
    End Sub

    Private Sub BtnRuler24Toggle_Checked(sender As Object, e As RoutedEventArgs)
        _ruler24Mode = True
        GenerateTimeRuler()
    End Sub

    Private Sub BtnRuler24Toggle_Unchecked(sender As Object, e As RoutedEventArgs)
        _ruler24Mode = False
        GenerateTimeRuler()
    End Sub

    Private Sub BtnMaximize_Click(sender As Object, e As RoutedEventArgs)
        Try
            If Me.WindowState = WindowState.Maximized Then
                Me.WindowState = WindowState.Normal
            Else
                Me.WindowState = WindowState.Maximized
            End If
        Catch
        End Try
    End Sub

    Private Sub BtnEditChannels_Click(sender As Object, e As RoutedEventArgs)
        Dim root = _rootFolderPath
        If String.IsNullOrWhiteSpace(root) OrElse Not Directory.Exists(root) Then
            MessageBox.Show("Select a valid root folder first.")
            Return
        End If

        Dim ed = New ChannelEditor(root)
        ed.Owner = Me
        ed.ShowDialog()

        ' After editor closes, reload the folder to reflect changes
        Dim t = LoadFolderAsync(root)
    End Sub

    Private Async Function LoadFolderAsync(path As String) As Task
        Try
            _rootFolderPath = path
            ' Use background scanner with progress and incremental updates
            _channels = New List(Of Channel)()
            LstChannels.ItemsSource = _channels

            Dim onChannel = Sub(ch As Channel)
                                Dispatcher.Invoke(Sub()
                                                      If Not _channels.Any(Function(x) x.Id = ch.Id) Then
                                                          _channels.Add(ch)
                                                          LstChannels.Items.Refresh()
                                                      Else
                                                          Dim existing = _channels.FirstOrDefault(Function(x) x.Id = ch.Id)
                                                          If existing IsNot Nothing Then
                                                              existing.Programs = ch.Programs
                                                              LstChannels.Items.Refresh()
                                                          End If
                                                      End If
                                                  End Sub)
            End Sub

            Dim scanned = Await TVMoviesChannelGuide.FolderScanner.ScanAsync(path, Me.Dispatcher, _libVLC, Nothing, onChannel)
            ' ensure list reflects final collection
            _channels = scanned
            LstChannels.ItemsSource = _channels
            ' persist last folder
            Try
                _settings.LastRootFolder = path
                _settings.Save()
            Catch
            End Try
        Catch ex As Exception
            MessageBox.Show("Error scanning folder: " & ex.Message)
        End Try
    End Function

    Private Async Function StartScanAsync(path As String) As Task
        Await LoadFolderAsync(path)
    End Function

    Private Sub LstChannels_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)
        Dim ch = TryCast(LstChannels.SelectedItem, Channel)
        If ch Is Nothing Then
            LstPrograms.ItemsSource = Nothing
            Return
        End If

        LstPrograms.ItemsSource = ch.Programs
        TxtProgramDetails.Text = BuildChannelVideoList(ch)
        UpdateSchedualFilesList(ch)
        UpdateTimeline(ch)
        ' update large logo
        Try
            If Not String.IsNullOrWhiteSpace(ch.LogoPath) Then
                Dim logoPath = If(Path.IsPathRooted(ch.LogoPath), ch.LogoPath, Path.Combine(_rootFolderPath, ch.LogoPath))
                If File.Exists(logoPath) Then
                    ImgChannelLogoLarge.Source = New System.Windows.Media.Imaging.BitmapImage(New Uri(logoPath))
                Else
                    ImgChannelLogoLarge.Source = Nothing
                End If
            Else
                ImgChannelLogoLarge.Source = Nothing
            End If
        Catch
        End Try
    End Sub

    Private Function BuildChannelVideoList(ch As Channel) As String
        If ch.Programs Is Nothing OrElse ch.Programs.Count = 0 Then
            Return $"{ch.Name} - no videos scheduled"
        End If

        Dim sb As New System.Text.StringBuilder()
        sb.AppendLine($"{ch.Name} - {ch.Programs.Count} video(s):")
        For Each p In ch.Programs
            sb.AppendLine($"{p.Start:ddd h:mm tt}  {p.Title}")
        Next
        Return sb.ToString().TrimEnd()
    End Function

    Private Sub UpdateSchedualFilesList(ch As Channel)
        Try
            If ch Is Nothing OrElse String.IsNullOrWhiteSpace(ch.FolderPath) Then
                SchedualFilesListView.ItemsSource = Nothing
                Return
            End If

            Dim schedualDir = Path.Combine(ch.FolderPath, "Schedual")
            If Not Directory.Exists(schedualDir) Then
                SchedualFilesListView.ItemsSource = Nothing
                Return
            End If

            Dim names = Directory.GetFiles(schedualDir, "*.mp4", SearchOption.TopDirectoryOnly).
                Select(Function(f) Path.GetFileName(f)).
                OrderBy(Function(n) n).
                ToList()
            SchedualFilesListView.ItemsSource = names
        Catch
            SchedualFilesListView.ItemsSource = Nothing
        End Try
    End Sub

    Private Sub SchedualFilesListView_MouseDoubleClick(sender As Object, e As System.Windows.Input.MouseButtonEventArgs)
        Try
            Dim fileName = TryCast(SchedualFilesListView.SelectedItem, String)
            If String.IsNullOrWhiteSpace(fileName) Then Return

            Dim ch = TryCast(LstChannels.SelectedItem, Channel)
            If ch Is Nothing OrElse String.IsNullOrWhiteSpace(ch.FolderPath) Then Return

            Dim fullPath = Path.Combine(ch.FolderPath, "Schedual", fileName)
            If Not File.Exists(fullPath) Then
                MessageBox.Show("File not found: " & fullPath)
                Return
            End If

            ' Play in the embedded VideoView
            PlayFile(fullPath, TimeSpan.Zero)
            TxtProgramDetails.Text = "Playing: " & Path.GetFileNameWithoutExtension(fileName)

            ' Also open/play the same file in the standalone Player.xaml window
            If _schedualPlayer Is Nothing OrElse Not _schedualPlayer.IsLoaded Then
                _schedualPlayer = New Player()
                _schedualPlayer.Owner = Me
                CloseDuplicateWindows(_schedualPlayer)
                _schedualPlayer.Show()
            End If
            _schedualPlayer.Activate()
            _schedualPlayer.PlayFile(fullPath)
        Catch ex As Exception
            MessageBox.Show("Playback error: " & ex.Message)
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

    Private Sub BtnMute_Click(sender As Object, e As RoutedEventArgs)
        Try
            If _vlcPlayer Is Nothing Then Return
            _vlcPlayer.Mute = Not _vlcPlayer.Mute
            BtnMute.Content = If(_vlcPlayer.Mute, "Unmute", "Mute")
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

    Private Sub UpdateTimeline(ch As Channel)
        Try
            If ch Is Nothing Then
                TimelineListView.ItemsSource = Nothing
                Return
            End If

            Dim now = DateTime.Now
            Dim blocks = New List(Of TimelineBlock)()
            For Each p In ch.Programs
                blocks.Add(New TimelineBlock With {.Title = p.Title, .TimeText = $"{p.Start:ddd h:mm tt} - {p.EndTime:h:mm tt}", .ToolTip = $"{p.Start:t} - {p.EndTime:t} ({p.Duration})", .Program = p, .IsCurrent = (p.Start <= now AndAlso p.EndTime > now)})
            Next

            TimelineListView.ItemsSource = blocks
        Catch
        End Try
    End Sub

    Private Sub LstPrograms_MouseDoubleClick(sender As Object, e As System.Windows.Input.MouseButtonEventArgs)
        Dim p = TryCast(LstPrograms.SelectedItem, ProgramItem)
        If p Is Nothing Then Return
        PlayFile(p.FilePath, TimeSpan.Zero)
        UpdateProgramDetails(p)
    End Sub

    Private Sub BtnPlay_Click(sender As Object, e As RoutedEventArgs)
        Try
            ' Play the selected program if present, otherwise resume current media
            Dim p = TryCast(LstPrograms.SelectedItem, ProgramItem)
            If p IsNot Nothing Then
                PlayFile(p.FilePath, TimeSpan.Zero)
                UpdateProgramDetails(p)
                Return
            End If
            If _vlcPlayer IsNot Nothing Then _vlcPlayer.Play()
        Catch ex As Exception
            MessageBox.Show("Play error: " & ex.Message)
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
            If _vlcPlayer IsNot Nothing Then
                _vlcPlayer.Stop()
            End If
            If _currentMedia IsNot Nothing Then
                _currentMedia.Dispose()
                _currentMedia = Nothing
            End If
            If TxtNowPlaying IsNot Nothing Then TxtNowPlaying.Text = "(nothing playing)"
        Catch
        End Try
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

    Private Sub ScrubSlider_PreviewMouseDown(sender As Object, e As System.Windows.Input.MouseButtonEventArgs)
        _isScrubbing = True
    End Sub

    Private Sub ScrubSlider_PreviewMouseUp(sender As Object, e As System.Windows.Input.MouseButtonEventArgs)
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

    Private Sub BtnOpenExternalVlc_Click(sender As Object, e As RoutedEventArgs)
        Try
            Dim ch = TryCast(LstChannels.SelectedItem, Channel)
            If ch Is Nothing Then
                MessageBox.Show("Select a channel first.")
                Return
            End If

            Dim now = DateTime.Now
            Dim active = ch.Programs.FirstOrDefault(Function(p) p.Start <= now AndAlso p.EndTime > now)
            If active Is Nothing Then
                MessageBox.Show("Nothing scheduled on this channel right now.")
                Return
            End If

            Dim path = active.FilePath
            If String.IsNullOrWhiteSpace(path) OrElse Not File.Exists(path) Then
                MessageBox.Show("File not found: " & path)
                Return
            End If

            ' Seek external VLC to how far into the program "now" actually is
            Dim offsetSeconds = CInt(Math.Max(0, (now - active.Start).TotalSeconds))
            Dim args = $"""{path}"" --start-time={offsetSeconds}"

            Try
                ' Try to start vlc from PATH
                Process.Start("vlc", args)
            Catch
                ' Try common install path - Program Files
                Dim vlcPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "VideoLAN\VLC\vlc.exe")
                If Not File.Exists(vlcPath) Then
                    ' Try Program Files (x86)
                    vlcPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "VideoLAN\VLC\vlc.exe")
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

    Private Sub BtnPlayNowInPlayer_Click(sender As Object, e As RoutedEventArgs)
        Try
            Dim ch = TryCast(LstChannels.SelectedItem, Channel)
            If ch Is Nothing Then
                MessageBox.Show("Select a channel first.")
                Return
            End If

            Dim now = DateTime.Now
            Dim active = ch.Programs.FirstOrDefault(Function(p) p.Start <= now AndAlso p.EndTime > now)
            If active Is Nothing Then
                MessageBox.Show("Nothing scheduled on this channel right now.")
                Return
            End If

            If String.IsNullOrWhiteSpace(active.FilePath) OrElse Not File.Exists(active.FilePath) Then
                MessageBox.Show("File not found: " & active.FilePath)
                Return
            End If

            Dim offset = now - active.Start
            If offset < TimeSpan.Zero Then offset = TimeSpan.Zero

            If _schedualPlayer Is Nothing OrElse Not _schedualPlayer.IsLoaded Then
                _schedualPlayer = New Player()
                _schedualPlayer.Owner = Me
                CloseDuplicateWindows(_schedualPlayer)
                _schedualPlayer.Show()
            End If

            _schedualPlayer.Activate()
            _schedualPlayer.PlayFile(active.FilePath, offset)
            TxtProgramDetails.Text = "Now on " & ch.Name & ": " & active.Title
        Catch ex As Exception
            MessageBox.Show("Playback error: " & ex.Message)
        End Try
    End Sub

    Private Sub MovieTitle_MouseLeftButtonUp(sender As Object, e As System.Windows.Input.MouseButtonEventArgs)
        Dim tb = TryCast(sender, TextBlock)
        If tb Is Nothing OrElse String.IsNullOrWhiteSpace(tb.Text) Then Return

        Dim filePath = ResolveMoviePath(tb.Text)
        If filePath Is Nothing Then
            MessageBox.Show(tb.Text & ".mp4 not found.")
            Return
        End If

        PlayFile(filePath, TimeSpan.Zero)
        TxtProgramDetails.Text = "Playing: " & tb.Text
    End Sub

    Private Function ResolveMoviePath(title As String) As String
        Dim fileName = title & ".mp4"

        Dim libraryPath = System.IO.Path.Combine(DefaultMovieFolder, fileName)
        If File.Exists(libraryPath) Then Return libraryPath

        Dim localCopy = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName)
        If File.Exists(localCopy) Then Return localCopy

        localCopy = System.IO.Path.Combine(Environment.CurrentDirectory, fileName)
        If File.Exists(localCopy) Then Return localCopy

        Return Nothing
    End Function

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
            If TxtNowPlaying IsNot Nothing Then TxtNowPlaying.Text = System.IO.Path.GetFileNameWithoutExtension(path)
            If VolumeSlider IsNot Nothing Then _vlcPlayer.Volume = CInt(VolumeSlider.Value)
            If BtnSubtitles IsNot Nothing Then BtnSubtitles.Content = "CC: Off"
            If BtnMute IsNot Nothing Then BtnMute.Content = "Mute"
            If ScrubSlider IsNot Nothing Then ScrubSlider.Value = 0
            If TimeCurrentLabel IsNot Nothing Then TimeCurrentLabel.Text = "0:00"
            If TimeTotalLabel IsNot Nothing Then TimeTotalLabel.Text = "0:00"
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

            ' Keep the Timeline's "on air now" highlight bar in sync without
            ' rebuilding the whole list (which would reset scroll position).
            Dim timelineBlocks = TryCast(TimelineListView.ItemsSource, List(Of TimelineBlock))
            If timelineBlocks IsNot Nothing Then
                For Each b In timelineBlocks
                    b.IsCurrent = (b.Program Is active)
                Next
            End If

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
