Imports Models
Imports System.Linq
Imports LibVLCSharp.Shared
Imports System.Windows.Forms
Imports System.Threading.Tasks
Imports System.IO
Imports System.Windows.Threading
Imports System.Diagnostics

Class MainWindow
    Private _settings As UserSettings
    Private _timelineScale As Double = 4.0 ' pixels per minute

    Private Class TimelineBlock
        Public Property Title As String
        Public Property Width As Double
        Public Property ToolTip As String
        Public Property Program As ProgramItem
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
    Private _currentMedia As Media
    Private _rulerStart As DateTime
    Private _ruler24Mode As Boolean

    Public Sub New()
        InitializeComponent()

        ' Initialize LibVLC
        Core.Initialize()
        _libVLC = New LibVLC()
        _vlcPlayer = New MediaPlayer(_libVLC)
        VideoView.MediaPlayer = _vlcPlayer

        AddHandler BtnSelectFolder.Click, AddressOf BtnSelectFolder_Click
        AddHandler BtnEditChannels.Click, AddressOf BtnEditChannels_Click
        AddHandler MenuHome.Click, AddressOf MenuHome_Click
        AddHandler MenuChannelsGrid.Click, AddressOf MenuChannelsGrid_Click
        AddHandler MenuEditChannels.Click, AddressOf MenuEditChannels_Click
        AddHandler MenuGuide.Click, AddressOf MenuGuide_Click
        AddHandler LstChannels.SelectionChanged, AddressOf LstChannels_SelectionChanged
        AddHandler LstChannels.PreviewMouseWheel, AddressOf LstChannels_PreviewMouseWheel
        AddHandler LstPrograms.MouseDoubleClick, AddressOf LstPrograms_MouseDoubleClick
        AddHandler BtnPlay.Click, AddressOf BtnPlay_Click
        AddHandler BtnPause.Click, AddressOf BtnPause_Click
        AddHandler BtnStop.Click, AddressOf BtnStop_Click
        AddHandler BtnOpenExternalVlc.Click, AddressOf BtnOpenExternalVlc_Click

        _scheduler = New DispatcherTimer With {.Interval = TimeSpan.FromSeconds(5)}
        AddHandler _scheduler.Tick, AddressOf SchedulerTick
        _scheduler.Start()

        ' Load settings and auto-open last folder if present
        Try
            _settings = UserSettings.Load()
            If Not String.IsNullOrWhiteSpace(_settings.LastRootFolder) AndAlso Directory.Exists(_settings.LastRootFolder) Then
                TxtRootPath.Text = _settings.LastRootFolder
                ' start scan in background (do not await)
                Dim _task = StartScanAsync(_settings.LastRootFolder)
            End If
        Catch
        End Try
        ' If no channels loaded, generate 30 demo channels
        If _channels Is Nothing OrElse _channels.Count = 0 Then
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
            splash.Show()
            Me.Close()
        Catch
        End Try
    End Sub

    Private Sub MenuChannelsGrid_Click(sender As Object, e As RoutedEventArgs)
        Try
            Dim cg = New ChannelsGrid()
            cg.Owner = Me
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

    Private Sub ProgramBlock_Click(sender As Object, e As System.Windows.Input.MouseButtonEventArgs)
        Try
            Dim fe = TryCast(sender, FrameworkElement)
            If fe Is Nothing Then Return
            Dim block = TryCast(fe.DataContext, TimelineBlock)
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
                slots = 6 ' 2.5 hours => 6 half-hour slots
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
        Dim root = TxtRootPath.Text
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
            ' Use background scanner with progress and incremental updates
            IndexProgress.Value = 0
            TxtIndexStatus.Text = "Scanning..."
            _channels = New List(Of Channel)()
            LstChannels.ItemsSource = _channels

            Dim progress = New Progress(Of Integer)(Sub(v)
                                                       IndexProgress.Value = v
                                                       TxtIndexStatus.Text = $"Scanning... {v}%"
                                                   End Sub)

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

            Dim onProgramUpdated = Sub(channelParam As Channel, progParam As ProgramItem)
                                       Dispatcher.Invoke(Sub()
                                                             Dim sel = TryCast(LstChannels.SelectedItem, Channel)
                                                             If sel IsNot Nothing AndAlso sel.Id = channelParam.Id Then
                                                                 LstPrograms.Items.Refresh()
                                                             End If
                                                         End Sub)
            End Sub

            Dim scanned = Await TVMoviesChannelGuide.FolderScanner.ScanAsync(path, Me.Dispatcher, _libVLC, progress, onChannel, onProgramUpdated)
            ' ensure list reflects final collection
            _channels = scanned
            LstChannels.ItemsSource = _channels
            TxtRootPath.Text = path & " (" & _channels.Count & " channels)"
            TxtIndexStatus.Text = "Done"
            IndexProgress.Value = 100
            ' persist last folder
            Try
                _settings.LastRootFolder = path
                _settings.Save()
            Catch
            End Try
        Catch ex As Exception
            MessageBox.Show("Error scanning folder: " & ex.Message)
        Finally
            BtnSelectFolder.IsEnabled = True
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
        TxtProgramDetails.Text = "(select a program)"
        UpdateTimeline(ch)
        ' update large logo
        Try
            If Not String.IsNullOrWhiteSpace(ch.LogoPath) Then
                Dim logoPath = If(Path.IsPathRooted(ch.LogoPath), ch.LogoPath, Path.Combine(TxtRootPath.Text, ch.LogoPath))
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
                TimelinePanel.ItemsSource = Nothing
                Return
            End If

            Dim blocks = New List(Of TimelineBlock)()
            For Each p In ch.Programs
                Dim mins = Math.Max(1, Math.Round(p.Duration.TotalMinutes))
                Dim width = Math.Min(800, Math.Max(40, mins * _timelineScale))
                blocks.Add(New TimelineBlock With {.Title = p.Title, .Width = width, .ToolTip = $"{p.Start:t} - {p.EndTime:t} ({p.Duration})", .Program = p})
            Next

            TimelinePanel.ItemsSource = blocks
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
        Catch
        End Try
    End Sub

    Private Sub BtnOpenExternalVlc_Click(sender As Object, e As RoutedEventArgs)
        Try
            Dim p = TryCast(LstPrograms.SelectedItem, ProgramItem)
            Dim path As String = Nothing
            If p IsNot Nothing Then
                path = p.FilePath
            ElseIf _currentMedia IsNot Nothing Then
                path = New Uri(_currentMedia.Mrl).LocalPath
            End If

            If String.IsNullOrWhiteSpace(path) OrElse Not File.Exists(path) Then
                MessageBox.Show("No valid video selected to open in VLC.")
                Return
            End If

            Try
                ' Try to start vlc from PATH
                Process.Start("vlc", $"""{path}""")
            Catch
                ' Try common install path - Program Files
                Dim vlcPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "VideoLAN\VLC\vlc.exe")
                If Not File.Exists(vlcPath) Then
                    ' Try Program Files (x86)
                    vlcPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "VideoLAN\VLC\vlc.exe")
                End If
                If File.Exists(vlcPath) Then
                    Process.Start(vlcPath, $"""{path}""")
                Else
                    MessageBox.Show("VLC not found. Please install VLC or ensure 'vlc' is on PATH.")
                End If
            End Try
        Catch ex As Exception
            MessageBox.Show("Failed to open in VLC: " & ex.Message)
        End Try
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
