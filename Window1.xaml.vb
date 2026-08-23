Imports System.IO
Imports System.Linq
Imports System.Windows
Imports System.Windows.Threading

Public Class Window1
    Private Const ChannelsFolder As String = "J:\FandangoMovies\Movies\TVGUIDE\Channels"
    Private Const SlotHours As Double = 2.0 ' assumed length per movie, for scheduling display only

    ' Shared so that every row click across the app reuses the same
    ' Player window/VLC engine instead of stacking up a new one per click.
    Private Shared _sharedPlayer As Player

    Private Class MovieSlot
        Implements System.ComponentModel.INotifyPropertyChanged
        Public Event PropertyChanged As System.ComponentModel.PropertyChangedEventHandler Implements System.ComponentModel.INotifyPropertyChanged.PropertyChanged

        Public Property ChannelName As String
        Public Property Title As String
        Public Property TimeText As String
        Public Property FilePath As String
        Public Property Start As DateTime
        Public Property EndTime As DateTime

        Private _isCurrent As Boolean
        ''' True for whichever slot is airing right now - refreshed by _nowTimer so
        ''' the highlight bar tracks the schedule without rebuilding the whole list.
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

    Private _nowTimer As DispatcherTimer

    ' Channels handed in from MainWindow's already-scanned schedule, so the times
    ' shown here match the Timeline in MainWindow exactly instead of being
    ' independently (and differently) recomputed from the filesystem.
    Private _channels As List(Of Channel)

    Public Sub New()
        Me.New(Nothing)
    End Sub

    Public Sub New(channels As List(Of Channel))
        InitializeComponent()
        _channels = channels
        AddHandler [Exit].Click, AddressOf Exit_Click
        AddHandler BtnHome.Click, AddressOf BtnHome_Click
        LoadMovieList()

        _nowTimer = New DispatcherTimer With {.Interval = TimeSpan.FromSeconds(5)}
        AddHandler _nowTimer.Tick, AddressOf RefreshNowPlaying
        _nowTimer.Start()
    End Sub

    ''' Recomputes which slot is airing right now without rebuilding the whole
    ''' list, so the highlight bar stays accurate as the schedule moves on.
    Private Sub RefreshNowPlaying(sender As Object, e As EventArgs)
        Dim slots = TryCast(MovieList.ItemsSource, List(Of MovieSlot))
        If slots Is Nothing Then Return
        Dim now = DateTime.Now
        For Each s In slots
            s.IsCurrent = (s.Start <= now AndAlso s.EndTime > now)
        Next
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

    Private Sub LoadMovieList()
        ' Prefer the schedule MainWindow already scanned (real per-file durations,
        ' same Start/EndTime used by its Timeline) so the times here match it
        ' exactly instead of being recomputed from scratch with different rules.
        If _channels IsNot Nothing AndAlso _channels.Count > 0 Then
            LoadMovieListFromChannels(_channels)
            Return
        End If

        Dim allSlots As New List(Of MovieSlot)()
        Try
            Dim channelsRoot = GetAutoDetectedRootFolder()
            If channelsRoot Is Nothing Then channelsRoot = ChannelsFolder
            If Directory.Exists(channelsRoot) Then
                Dim channelDirs = Directory.GetDirectories(channelsRoot).
                    OrderBy(Function(d) ExtractChannelNumber(Path.GetFileName(d))).
                    ThenBy(Function(d) d, StringComparer.OrdinalIgnoreCase).
                    ToArray()

                For Each channelDir In channelDirs
                    Dim chName = Path.GetFileName(channelDir)

                    ' A channel's playlist can live directly in its folder and/or in its
                    ' "Schedual" subfolder - combine whatever is there, same as MainWindow.
                    Dim files = Directory.GetFiles(channelDir, "*.mp4", SearchOption.TopDirectoryOnly).ToList()
                    Dim schedualDir = Path.Combine(channelDir, "Schedual")
                    If Directory.Exists(schedualDir) Then
                        files.AddRange(Directory.GetFiles(schedualDir, "*.mp4", SearchOption.TopDirectoryOnly))
                    End If

                    Dim cursor = DateTime.Now
                    Dim minuteOffset = cursor.Minute Mod 30
                    If minuteOffset <> 0 Then cursor = cursor.AddMinutes(30 - minuteOffset)
                    cursor = New DateTime(cursor.Year, cursor.Month, cursor.Day, cursor.Hour, cursor.Minute, 0)

                    For Each f In files
                        Dim title = Path.GetFileNameWithoutExtension(f)
                        Dim start = cursor
                        Dim finish = cursor.AddHours(SlotHours)
                        allSlots.Add(New MovieSlot With {
                            .ChannelName = chName,
                            .Title = title,
                            .TimeText = $"{start:ddd h:mm tt} - {finish:h:mm tt}",
                            .FilePath = f,
                            .Start = start,
                            .EndTime = finish,
                            .IsCurrent = (start <= DateTime.Now AndAlso finish > DateTime.Now)
                        })
                        cursor = finish
                    Next
                Next
            End If
        Catch
        End Try
        MovieList.ItemsSource = allSlots
    End Sub

    ''' Builds the movie list straight from already-scanned channels/programs (same
    ''' data MainWindow's Timeline is built from) so displayed times always agree.
    Private Sub LoadMovieListFromChannels(channels As List(Of Channel))
        Dim allSlots As New List(Of MovieSlot)()
        Dim now = DateTime.Now
        For Each ch In channels
            For Each p In ch.Programs
                allSlots.Add(New MovieSlot With {
                    .ChannelName = ch.Name,
                    .Title = p.Title,
                    .TimeText = $"{p.Start:ddd h:mm tt} - {p.EndTime:h:mm tt}",
                    .FilePath = p.FilePath,
                    .Start = p.Start,
                    .EndTime = p.EndTime,
                    .IsCurrent = (p.Start <= now AndAlso p.EndTime > now)
                })
            Next
        Next
        MovieList.ItemsSource = allSlots
    End Sub

    ''' Returns the first integer found anywhere in a folder name - "Channel 12" -> 12,
    ''' "Ch10 Tremors" -> 10 - or Integer.MaxValue if it has none, so channels sort in
    ''' natural numeric order.
    Private Function ExtractChannelNumber(name As String) As Integer
        Dim m = System.Text.RegularExpressions.Regex.Match(name, "\d+")
        If m.Success Then
            Dim n As Integer
            If Integer.TryParse(m.Value, n) Then Return n
        End If
        Return Integer.MaxValue
    End Function

    Private Sub Exit_Click(sender As Object, e As RoutedEventArgs)
        Application.Current.Shutdown()
    End Sub

    Private Sub MovieRow_MouseLeftButtonUp(sender As Object, e As Input.MouseButtonEventArgs)
        Dim fe = TryCast(sender, FrameworkElement)
        If fe Is Nothing Then Return
        Dim slot = TryCast(fe.DataContext, MovieSlot)
        If slot Is Nothing OrElse String.IsNullOrWhiteSpace(slot.FilePath) OrElse Not File.Exists(slot.FilePath) Then Return

        If _sharedPlayer Is Nothing OrElse Not _sharedPlayer.IsLoaded Then
            _sharedPlayer = New Player()
            AddHandler _sharedPlayer.Closed, AddressOf SharedPlayer_Closed
            _sharedPlayer.Owner = Me
            CloseDuplicateWindows(_sharedPlayer)
            _sharedPlayer.Show()
        End If

        _sharedPlayer.Activate()
        _sharedPlayer.PlayFile(slot.FilePath)
    End Sub

    Private Sub SharedPlayer_Closed(sender As Object, e As EventArgs)
        _sharedPlayer = Nothing
    End Sub
End Class
