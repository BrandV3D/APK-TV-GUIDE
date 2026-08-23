Imports LibVLCSharp.Shared
Imports System.IO
Imports System.Linq
Imports System.Threading.Tasks
Imports System.Windows.Media
Imports System.Windows.Threading

''' A classic grid-style "live TV" channel guide: a fixed column of 15 channels
''' down the left, and a horizontally-scrollable 24-hour timeline of program
''' blocks to the right (built from the same scanned schedule data as the rest
''' of the app), with a live preview of whichever channel is selected.
Public Class LiveTV
    Private Const ChannelCount As Integer = 15
    Private Const PixelsPerHour As Double = 220.0
    Private Const HeaderRowHeight As Double = 28.0
    Private Const ChannelRowHeight As Double = 30.0
    Private Const DefaultChannelsFolder As String = "J:\FandangoMovies\Movies\TVGUIDE\Channels"

    Private _libVLC As LibVLC
    Private _vlcPlayer As LibVLCSharp.Shared.MediaPlayer
    Private _currentMedia As Media
    Private _channels As List(Of Channel)
    Private _rootFolder As String
    Private _selectedChannelIndex As Integer = 0
    Private _categoryFilter As String = "All"
    Private _currentProgram As ProgramItem
    ' Shared across windows that can launch Player, so clicking "Play in Player"
    ' more than once reuses the same window/VLC engine instead of stacking up new ones.
    Private Shared _sharedPlayer As Player

    Private _clockTimer As DispatcherTimer
    Private _guideTimer As DispatcherTimer
    Private _scrubTimer As DispatcherTimer
    Private _isScrubbing As Boolean

    Public Sub New()
        InitializeComponent()

        Core.Initialize()
        _libVLC = New LibVLC()
        _vlcPlayer = New LibVLCSharp.Shared.MediaPlayer(_libVLC)
        PreviewVideo.MediaPlayer = _vlcPlayer

        AddHandler BtnCloseGuide.Click, Sub() Me.Close()
        AddHandler BtnHome.Click, AddressOf BtnHome_Click
        AddHandler BtnMenuHome.Click, AddressOf BtnHome_Click
        AddHandler BtnMenuTvGuide.Click, AddressOf BtnMenuTvGuide_Click
        AddHandler BtnMenuProgramming.Click, AddressOf BtnMenuProgramming_Click
        AddHandler BtnMenuEditChannels.Click, AddressOf BtnMenuEditChannels_Click
        AddHandler BtnCategoryAll.Click, Sub() SetCategoryFilter("All")
        AddHandler BtnCategoryMovies.Click, Sub() SetCategoryFilter("Movies")
        AddHandler BtnCategoryShows.Click, Sub() SetCategoryFilter("Shows")
        AddHandler Time_Text.Click, AddressOf BtnJumpToNow_Click
        AddHandler Edit.Click, AddressOf BtnFullScreen_Click
        AddHandler BtnPlayInPlayer.Click, AddressOf BtnPlayInPlayer_Click
        AddHandler BtnPlay.Click, AddressOf BtnPlay_Click
        AddHandler BtnPause.Click, AddressOf BtnPause_Click
        AddHandler BtnStop.Click, AddressOf BtnStop_Click
        AddHandler BtnMute.Click, AddressOf BtnMute_Click
        AddHandler BtnSubtitles.Click, AddressOf BtnSubtitles_Click

        _scrubTimer = New DispatcherTimer With {.Interval = TimeSpan.FromMilliseconds(500)}
        AddHandler _scrubTimer.Tick, AddressOf ScrubTimerTick
        _scrubTimer.Start()

        AddHandler Me.Loaded, AddressOf LiveTV_Loaded
        AddHandler Me.Closed, AddressOf LiveTV_Closed
    End Sub

    Private Sub LiveTV_Loaded(sender As Object, e As EventArgs)
        ' Reuse whatever the main guide already scanned, same as TV_Guide/Window1 do,
        ' so channel numbers/schedules agree everywhere in the app.
        Dim mw = TryCast(Me.Owner, MainWindow)
        If mw IsNot Nothing AndAlso mw.Channels IsNot Nothing AndAlso mw.Channels.Count > 0 Then
            _channels = mw.Channels
            AfterChannelsReady()
            Return
        End If

        _channels = New List(Of Channel)()
        Dim settings = UserSettings.Load()
        _rootFolder = GetAutoDetectedRootFolder()
        If _rootFolder Is Nothing Then
            _rootFolder = If(Not String.IsNullOrWhiteSpace(settings.LastRootFolder) AndAlso Directory.Exists(settings.LastRootFolder), settings.LastRootFolder, DefaultChannelsFolder)
        End If
        If Directory.Exists(_rootFolder) Then
            Dim task = ScanOwnChannelsAsync(_rootFolder)
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
        RebuildGuide()
        UpdateSelectedChannelPreview()
        StartTimers()

        ' Land the guide on "now" to start, with the rest of the 24 hours scrollable
        ' both ways from there - matches how a real cable guide opens.
        Dim now = DateTime.Now
        Dim offset = now.TimeOfDay.TotalHours * PixelsPerHour
        TimeScrollViewer.ScrollToHorizontalOffset(offset)
    End Sub

    ''' The channel list and time grid live in separate nested ScrollViewers (one
    ''' vertical-only, one horizontal-only) so they can scroll independently. A
    ''' plain ScrollViewer always marks a MouseWheel event handled even when it has
    ''' nothing to scroll in that axis, which silently swallows wheel input over the
    ''' time grid. Handle it once here instead: plain wheel scrolls the guide up/down,
    ''' Shift+wheel scrolls the time grid left/right.
    Private Sub ChannelListScrollViewer_PreviewMouseWheel(sender As Object, e As MouseWheelEventArgs)
        If Keyboard.Modifiers = ModifierKeys.Shift Then
            TimeScrollViewer.ScrollToHorizontalOffset(TimeScrollViewer.HorizontalOffset - e.Delta)
        Else
            ChannelListScrollViewer.ScrollToVerticalOffset(ChannelListScrollViewer.VerticalOffset - e.Delta)
        End If
        e.Handled = True
    End Sub

    Private Sub StartTimers()
        UpdateClock()
        _clockTimer = New DispatcherTimer With {.Interval = TimeSpan.FromSeconds(1)}
        AddHandler _clockTimer.Tick, Sub() UpdateClock()
        _clockTimer.Start()

        _guideTimer = New DispatcherTimer With {.Interval = TimeSpan.FromSeconds(30)}
        AddHandler _guideTimer.Tick, Sub()
                                          RebuildGuide()
                                          UpdateSelectedChannelPreview()
                                      End Sub
        _guideTimer.Start()
    End Sub

    Private Sub UpdateClock()
        Dim now = DateTime.Now
        Current_Time_with_Am_or_PM.Text = now.ToString("h:mm:ss tt")
        Curent_Date.Text = now.ToString("dddd, MMM d")
        Day_of_the_Week_and_Date.Text = now.ToString("ddd M/d")
    End Sub

    ''' Channel N (real programming, if a folder was scanned for it) or an empty
    ''' placeholder if fewer than 15 channels were found - the guide always shows
    ''' exactly 15 rows, "Channel 1" through "Channel 15", top to bottom.
    Private Function ChannelAt(index As Integer) As Channel
        If _channels IsNot Nothing AndAlso index < _channels.Count Then Return _channels(index)
        Return Nothing
    End Function

    ''' "All" keeps every one of the 15 rows; "Movies"/"Shows" only keeps channels
    ''' whose program on right now is longer/shorter than 40 minutes - a channel
    ''' with nothing airing (or no data at all) drops out of both those filters.
    Private Function GetFilteredIndices() As List(Of Integer)
        Dim result As New List(Of Integer)()
        Dim now = DateTime.Now
        For i = 0 To ChannelCount - 1
            If _categoryFilter = "All" Then
                result.Add(i)
                Continue For
            End If
            Dim ch = ChannelAt(i)
            Dim active = ch?.Programs.FirstOrDefault(Function(p) p.Start <= now AndAlso p.EndTime > now)
            If active Is Nothing Then Continue For
            Dim isMovie = active.Duration.TotalMinutes > 40
            If (_categoryFilter = "Movies" AndAlso isMovie) OrElse (_categoryFilter = "Shows" AndAlso Not isMovie) Then
                result.Add(i)
            End If
        Next
        Return result
    End Function

    Private Sub SetCategoryFilter(category As String)
        _categoryFilter = category
        Dim activeBg As Brush = New SolidColorBrush(Color.FromRgb(&H5D, &HA9, &HFF))
        Dim inactiveBg As Brush = New SolidColorBrush(Color.FromRgb(&H22, &H33, &H44))
        BtnCategoryAll.Background = If(category = "All", activeBg, inactiveBg)
        BtnCategoryAll.Foreground = If(category = "All", Brushes.Black, Brushes.White)
        BtnCategoryMovies.Background = If(category = "Movies", activeBg, inactiveBg)
        BtnCategoryMovies.Foreground = If(category = "Movies", Brushes.Black, Brushes.White)
        BtnCategoryShows.Background = If(category = "Shows", activeBg, inactiveBg)
        BtnCategoryShows.Foreground = If(category = "Shows", Brushes.Black, Brushes.White)
        RebuildGuide()
    End Sub

    Private Sub RebuildGuide()
        ' Rebuilding clears/re-adds every row, which collapses the ScrollViewer's
        ' content for a moment and makes it snap back to the top - save/restore
        ' the offset so the 30-second guide-refresh timer doesn't keep kicking
        ' the view back up to Channel 1 while scrolled down toward 11-15.
        Dim savedOffset = ChannelListScrollViewer.VerticalOffset
        Dim indices = GetFilteredIndices()
        BuildChannelColumn(indices)
        BuildTimeGrid(indices)
        ' The content Grid's height was previously hardcoded and shorter than the
        ' full row list, which capped the ScrollViewer's scrollable extent and cut
        ' off the last few channels (12-15) - size it to the actual row total instead.
        ' Add room for TimeScrollViewer's own horizontal scrollbar strip: it renders
        ' inside that ScrollViewer's declared height, so without this the scrollbar
        ' would eat into the last row instead of getting its own space.
        GuideContentGrid.Height = HeaderRowHeight + indices.Count * ChannelRowHeight + SystemParameters.HorizontalScrollBarHeight
        ChannelListScrollViewer.ScrollToVerticalOffset(savedOffset)
    End Sub

    Private Sub BuildChannelColumn(indices As List(Of Integer))
        ChannelColumnHost.RowDefinitions.Clear()
        ChannelColumnHost.Children.Clear()
        ChannelColumnHost.RowDefinitions.Add(New RowDefinition With {.Height = New GridLength(HeaderRowHeight)})

        Dim nowBrush = TryCast(Application.Current.TryFindResource("NowPlayingBrush"), Brush)
        Dim nowFg = TryCast(Application.Current.TryFindResource("NowPlayingForeground"), Brush)

        For rowPos = 0 To indices.Count - 1
            Dim idx = indices(rowPos)
            ChannelColumnHost.RowDefinitions.Add(New RowDefinition With {.Height = New GridLength(ChannelRowHeight)})
            Dim isSelected = (idx = _selectedChannelIndex)
            Dim border As New Border With {
                .Background = If(isSelected, nowBrush, New SolidColorBrush(Color.FromRgb(0, 27, 47))),
                .BorderBrush = New SolidColorBrush(Color.FromRgb(11, 43, 69)),
                .BorderThickness = New Thickness(0, 0, 0, 1),
                .Cursor = Cursors.Hand,
                .Tag = idx
            }
            Dim tb As New TextBlock With {
                .Text = "Channel " & (idx + 1),
                .Foreground = If(isSelected, nowFg, Brushes.Gold),
                .FontWeight = FontWeights.Bold,
                .FontFamily = New FontFamily("Consolas"),
                .VerticalAlignment = VerticalAlignment.Center,
                .Margin = New Thickness(10, 0, 4, 0),
                .TextTrimming = TextTrimming.CharacterEllipsis
            }
            border.Child = tb
            AddHandler border.MouseLeftButtonUp, AddressOf ChannelRow_Click
            Grid.SetRow(border, rowPos + 1)
            ChannelColumnHost.Children.Add(border)
        Next
    End Sub

    Private Sub BuildTimeGrid(indices As List(Of Integer))
        TimeGridHost.RowDefinitions.Clear()
        TimeGridHost.Children.Clear()

        Dim totalWidth = 24.0 * PixelsPerHour
        TimeGridHost.Width = totalWidth
        TimeGridHost.RowDefinitions.Add(New RowDefinition With {.Height = New GridLength(HeaderRowHeight)})

        Dim dayStart = DateTime.Today
        Dim dayEnd = dayStart.AddDays(1)
        Dim now = DateTime.Now

        ' Half-hour ruler across the full 24 hours - horizontally scrollable.
        Dim rulerCanvas As New Canvas With {.Width = totalWidth, .Height = HeaderRowHeight}
        Dim cellW = PixelsPerHour / 2.0
        For half = 0 To 47
            Dim t = dayStart.AddMinutes(half * 30)
            Dim cell As New Border With {
                .Width = cellW, .Height = HeaderRowHeight,
                .Background = New SolidColorBrush(Color.FromRgb(1, 79, 152)),
                .BorderBrush = New SolidColorBrush(Color.FromRgb(11, 43, 69)),
                .BorderThickness = New Thickness(0, 0, 1, 0)
            }
            cell.Child = New TextBlock With {
                .Text = t.ToString("h:mm tt"), .Foreground = Brushes.White, .FontWeight = FontWeights.Bold,
                .FontFamily = New FontFamily("Consolas"), .FontSize = 12,
                .HorizontalAlignment = HorizontalAlignment.Left, .VerticalAlignment = VerticalAlignment.Center
            }
            Canvas.SetLeft(cell, half * cellW)
            Canvas.SetTop(cell, 0)
            rulerCanvas.Children.Add(cell)
        Next
        Grid.SetRow(rulerCanvas, 0)
        TimeGridHost.Children.Add(rulerCanvas)

        Dim nowBrush = TryCast(Application.Current.TryFindResource("NowPlayingBrush"), Brush)
        Dim nowFg = TryCast(Application.Current.TryFindResource("NowPlayingForeground"), Brush)

        For rowPos = 0 To indices.Count - 1
            Dim idx = indices(rowPos)
            TimeGridHost.RowDefinitions.Add(New RowDefinition With {.Height = New GridLength(ChannelRowHeight)})
            Dim rowCanvas As New Canvas With {.Width = totalWidth, .Height = ChannelRowHeight, .Background = Brushes.Transparent}

            Dim ch = ChannelAt(idx)
            If ch IsNot Nothing Then
                For Each p In ch.Programs
                    If p.EndTime <= dayStart OrElse p.Start >= dayEnd Then Continue For
                    Dim segStart = If(p.Start < dayStart, dayStart, p.Start)
                    Dim segEnd = If(p.EndTime > dayEnd, dayEnd, p.EndTime)
                    Dim left = (segStart - dayStart).TotalHours * PixelsPerHour
                    Dim w = Math.Max(2.0, (segEnd - segStart).TotalHours * PixelsPerHour - 2.0)
                    Dim isNow = (p.Start <= now AndAlso p.EndTime > now)

                    Dim block As New Border With {
                        .Width = w, .Height = ChannelRowHeight - 2,
                        .Background = If(isNow, nowBrush, New SolidColorBrush(Color.FromRgb(&H12, &H3B, &H5A))),
                        .BorderBrush = New SolidColorBrush(Color.FromRgb(11, 43, 69)),
                        .BorderThickness = New Thickness(1),
                        .CornerRadius = New CornerRadius(2),
                        .ToolTip = $"{p.Start:h:mm tt} - {p.EndTime:h:mm tt}: {p.Title}"
                    }
                    block.Child = New TextBlock With {
                        .Text = p.Title, .Foreground = If(isNow, nowFg, Brushes.White),
                        .FontSize = 12, .Margin = New Thickness(5, 3, 4, 0),
                        .TextTrimming = TextTrimming.CharacterEllipsis, .VerticalAlignment = VerticalAlignment.Top
                    }
                    Canvas.SetLeft(block, left)
                    Canvas.SetTop(block, 1)
                    rowCanvas.Children.Add(block)
                Next
            End If

            Grid.SetRow(rowCanvas, rowPos + 1)
            TimeGridHost.Children.Add(rowCanvas)
        Next

        ' Vertical "now" line across the ruler and every channel row, like a real
        ' Prevue-style guide, so it's obvious at a glance where "now" falls.
        Dim nowOffset = (now - dayStart).TotalHours * PixelsPerHour
        Dim nowLine As New Rectangle With {
            .Width = 2, .Fill = Brushes.White, .IsHitTestVisible = False,
            .HorizontalAlignment = HorizontalAlignment.Left,
            .Margin = New Thickness(nowOffset, 0, 0, 0)
        }
        Grid.SetRow(nowLine, 0)
        Grid.SetRowSpan(nowLine, indices.Count + 1)
        TimeGridHost.Children.Add(nowLine)
    End Sub

    Private Sub ChannelRow_Click(sender As Object, e As Input.MouseButtonEventArgs)
        Dim border = TryCast(sender, Border)
        If border Is Nothing Then Return
        _selectedChannelIndex = CInt(border.Tag)
        RebuildGuide()
        UpdateSelectedChannelPreview()
    End Sub

    Private Sub UpdateSelectedChannelPreview()
        Try
            Dim ch = ChannelAt(_selectedChannelIndex)
            SelectedChannelNameText.Text = "Channel " & (_selectedChannelIndex + 1)
            ComingUpTodayPanel.Children.Clear()

            If ch Is Nothing Then
                SelectedProgramTitleText.Text = "(no schedule)"
                Return
            End If

            Dim now = DateTime.Now
            Dim active = ch.Programs.FirstOrDefault(Function(p) p.Start <= now AndAlso p.EndTime > now)
            _currentProgram = active
            SelectedProgramTitleText.Text = If(active IsNot Nothing, active.Title, "(off air)")

            For Each p In ch.Programs.Where(Function(x) x.Start >= now).OrderBy(Function(x) x.Start).Take(12)
                Dim tb As New TextBlock With {.Margin = New Thickness(0, 0, 0, 4), .TextWrapping = TextWrapping.Wrap, .FontSize = 12}
                tb.Inlines.Add(New Documents.Run(p.Title) With {.FontWeight = FontWeights.Bold, .Foreground = Brushes.White})
                tb.Inlines.Add(New Documents.Run($"  {p.Start:h:mm tt}") With {.FontSize = 11, .Foreground = Brushes.LightSkyBlue})
                ComingUpTodayPanel.Children.Add(tb)
            Next

            If active Is Nothing OrElse Not File.Exists(active.FilePath) Then Return

            ' Only (re)start playback when the file actually changed - the periodic
            ' guide refresh (every 30s) would otherwise restart/reseek the same
            ' program over and over, stuttering the preview and fighting the user's
            ' own play/pause/mute/scrub controls.
            Dim needPlay = _currentMedia Is Nothing OrElse Not String.Equals(_currentMedia.Mrl, New Uri(active.FilePath).AbsoluteUri, StringComparison.OrdinalIgnoreCase)
            If Not needPlay Then Return

            If _currentMedia IsNot Nothing Then
                _currentMedia.Dispose()
                _currentMedia = Nothing
            End If
            _currentMedia = New Media(_libVLC, active.FilePath, FromType.FromPath)
            _vlcPlayer.Play(_currentMedia)
            _vlcPlayer.Volume = CInt(VolumeSlider.Value)
            _vlcPlayer.Mute = False
            BtnMute.Content = "Mute"
            BtnSubtitles.Content = "CC: Off"
            ScrubSlider.Value = 0
            TimeCurrentLabel.Text = "0:00"
            TimeTotalLabel.Text = "0:00"

            Dim offset = now - active.Start
            If offset > TimeSpan.Zero Then
                Task.Delay(250).ContinueWith(Sub()
                                                  Try
                                                      If _vlcPlayer IsNot Nothing Then _vlcPlayer.Time = CInt(offset.TotalMilliseconds)
                                                  Catch
                                                  End Try
                                              End Sub, TaskScheduler.Default)
            End If
        Catch
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

    ''' Opens (or reuses) the app's own Player window and plays the selected
    ''' channel's currently-airing program in it, seeked to how far into it
    ''' "now" actually is - same shared-player approach Window1 uses.
    Private Sub BtnPlayInPlayer_Click(sender As Object, e As RoutedEventArgs)
        Try
            If _currentProgram Is Nothing Then
                MessageBox.Show("Nothing airing right now on this channel.")
                Return
            End If

            Dim path = _currentProgram.FilePath
            If String.IsNullOrWhiteSpace(path) OrElse Not File.Exists(path) Then
                MessageBox.Show("File not found: " & path)
                Return
            End If

            If _sharedPlayer Is Nothing OrElse Not _sharedPlayer.IsLoaded Then
                _sharedPlayer = New Player()
                _sharedPlayer.Owner = Me
                CloseDuplicateWindows(_sharedPlayer)
                _sharedPlayer.Show()
            End If

            _sharedPlayer.Activate()
            Dim offset = DateTime.Now - _currentProgram.Start
            _sharedPlayer.PlayFile(path, If(offset > TimeSpan.Zero, offset, TimeSpan.Zero))
        Catch ex As Exception
            MessageBox.Show("Failed to open in Player: " & ex.Message)
        End Try
    End Sub

    Private Sub BtnJumpToNow_Click(sender As Object, e As RoutedEventArgs)
        Dim offset = DateTime.Now.TimeOfDay.TotalHours * PixelsPerHour
        TimeScrollViewer.ScrollToHorizontalOffset(offset)
    End Sub

    Private Sub BtnFullScreen_Click(sender As Object, e As RoutedEventArgs)
        Me.WindowState = If(Me.WindowState = WindowState.Maximized, WindowState.Normal, WindowState.Maximized)
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

    Private Sub BtnMenuTvGuide_Click(sender As Object, e As RoutedEventArgs)
        Try
            Dim w = New TV_Guide()
            w.Owner = Me
            CloseDuplicateWindows(w)
            w.Show()
        Catch
        End Try
    End Sub

    Private Sub BtnMenuProgramming_Click(sender As Object, e As RoutedEventArgs)
        Try
            Dim w = New Window1(_channels)
            w.Owner = Me
            CloseDuplicateWindows(w)
            w.Show()
        Catch
        End Try
    End Sub

    Private Sub BtnMenuEditChannels_Click(sender As Object, e As RoutedEventArgs)
        Try
            Dim root = If(_rootFolder, GetAutoDetectedRootFolder())
            If String.IsNullOrWhiteSpace(root) Then root = DefaultChannelsFolder
            Dim w = New ChannelEditor(root)
            w.Owner = Me
            CloseDuplicateWindows(w)
            w.Show()
        Catch
        End Try
    End Sub

    Private Sub LiveTV_Closed(sender As Object, e As EventArgs)
        Try
            _clockTimer?.Stop()
            _guideTimer?.Stop()
            _scrubTimer?.Stop()
            _vlcPlayer?.Stop()
            _currentMedia?.Dispose()
            _vlcPlayer?.Dispose()
            _libVLC?.Dispose()
        Catch
        End Try
    End Sub
End Class
