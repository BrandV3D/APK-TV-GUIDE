Imports System.IO
Imports System.Threading.Tasks
Imports System.Linq
Imports System.Windows.Threading
Imports LibVLCSharp.Shared
Imports System.Text.Json

Namespace TVMoviesChannelGuide
    Public Class FolderScanner
        Private Shared ReadOnly _videoExtensions As String() = {".mp4"}

        Public Shared Async Function ScanAsync(rootPath As String, dispatcher As Dispatcher, libVLC As LibVLC, progress As IProgress(Of Integer), onChannelScanned As Action(Of Channel)) As Task(Of System.Collections.Generic.List(Of Channel))
            Dim result = New System.Collections.Generic.List(Of Channel)()
            If Not Directory.Exists(rootPath) Then Return result

            Dim cache = New DurationCache(rootPath)
            Dim rnd = New Random()
            Dim fillerClips = Await LoadFillerClipsAsync(libVLC)
            Dim showsClips = Await LoadShowsClipsAsync(libVLC)

            ' simple channels.json support: entries with folder or files
            Dim configPath = System.IO.Path.Combine(rootPath, "channels.json")
            If File.Exists(configPath) Then
                Try
                    Dim txt = File.ReadAllText(configPath)
                    Dim arr = JsonDocument.Parse(txt).RootElement.EnumerateArray()
                    Dim idx = 0
                    For Each el In arr
                        Dim name As String = Nothing
                        Dim filesList As New List(Of String)()
                        Dim logo As String = Nothing
                        Dim prop As JsonElement
                        If el.TryGetProperty("name", prop) Then name = prop.GetString()
                        If el.TryGetProperty("files", prop) Then
                            For Each f In prop.EnumerateArray()
                                Dim fp = f.GetString()
                                If Not String.IsNullOrWhiteSpace(fp) Then
                                    If Not Path.IsPathRooted(fp) Then fp = System.IO.Path.Combine(rootPath, fp)
                                    filesList.Add(fp)
                                End If
                            Next
                        ElseIf el.TryGetProperty("folder", prop) Then
                            Dim fld = prop.GetString()
                            Dim full = If(System.IO.Path.IsPathRooted(fld), fld, System.IO.Path.Combine(rootPath, fld))
                            If Directory.Exists(full) Then
                                filesList.AddRange(Directory.GetFiles(full, "*.*", SearchOption.TopDirectoryOnly).Where(Function(f) _videoExtensions.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant())).OrderBy(Function(f) f))
                            End If
                        End If
                        If el.TryGetProperty("logo", prop) Then logo = prop.GetString()
                        If Not String.IsNullOrWhiteSpace(logo) AndAlso Not System.IO.Path.IsPathRooted(logo) Then logo = System.IO.Path.Combine(rootPath, logo)

                        Dim ch = New Channel With {.Id = Guid.NewGuid().ToString(), .Name = If(String.IsNullOrWhiteSpace(name), "Channel " & (idx + 1).ToString(), name), .FolderPath = rootPath, .LogoPath = logo}
                        Dim cursor = DateTime.Today
                        For Each f In filesList.Where(Function(p) File.Exists(p) AndAlso System.IO.Path.GetExtension(p).ToLowerInvariant() = ".mp4")
                            Dim durSeconds As Double = 0
                            If Not cache.TryGet(f, durSeconds) Then
                                durSeconds = Await GetMediaDurationSecondsAsync(f, libVLC)
                                If durSeconds > 0 Then cache.SetDuration(f, durSeconds)
                            End If
                            Dim dur = TimeSpan.FromSeconds(durSeconds)
                            If dur = TimeSpan.Zero Then Continue For
                            Dim pItem = New ProgramItem With {.ChannelId = ch.Id, .Title = System.IO.Path.GetFileNameWithoutExtension(f), .Start = cursor, .EndTime = cursor.Add(dur), .FilePath = f, .FileName = System.IO.Path.GetFileName(f)}
                            ch.Programs.Add(pItem)
                            cursor = pItem.EndTime

                            Dim brk = BuildFillerBreak(ch.Id, cursor, fillerClips, rnd)
                            ch.Programs.AddRange(brk.Items)
                            cursor = brk.NewCursor
                        Next
                        result.Add(ch)
                        Try
                            onChannelScanned?.Invoke(ch)
                        Catch
                        End Try
                        idx += 1
                        If progress IsNot Nothing Then progress.Report(CInt((idx / CSng(arr.Count())) * 100))
                    Next
                    Return result
                Catch
                End Try
            End If

            ' fallback: each subfolder is a channel
            ' Sort numerically (Channel 2 before Channel 10) instead of the plain
            ' alphabetical order Directory.GetDirectories/OS enumeration gives, which
            ' would otherwise put Channel 10/11/12 ahead of Channel 2.
            Dim directories = Directory.GetDirectories(rootPath).
                OrderBy(Function(d) ExtractChannelNumber(System.IO.Path.GetFileName(d))).
                ThenBy(Function(d) d, StringComparer.OrdinalIgnoreCase).
                ToArray()
            If directories.Length > 0 Then
                For i = 0 To directories.Length - 1
                    Dim dir = directories(i)
                    Dim ch = New Channel With {.Id = Guid.NewGuid().ToString(), .Name = System.IO.Path.GetFileName(dir), .FolderPath = dir}

                    ' A channel's playlist can live either directly in its folder or in a
                    ' "Schedual" subfolder underneath it (or both) - combine whatever is there.
                    Dim schedualDir = System.IO.Path.Combine(dir, "Schedual")
                    Dim files = Directory.GetFiles(dir, "*.*", SearchOption.TopDirectoryOnly).
                        Where(Function(f) _videoExtensions.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant())).ToArray()
                    If Directory.Exists(schedualDir) Then
                        files = files.Concat(
                            Directory.GetFiles(schedualDir, "*.*", SearchOption.TopDirectoryOnly).
                                Where(Function(f) _videoExtensions.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant()))).ToArray()
                    End If
                    ' Resolve each local file's duration once up front so movies can
                    ' be grouped by length for the schedule pattern below.
                    Dim fileDurations As New List(Of (Path As String, Duration As TimeSpan))()
                    For Each f In files.Where(Function(p) System.IO.Path.GetExtension(p).ToLowerInvariant() = ".mp4")
                        Dim durSeconds As Double = 0
                        If Not cache.TryGet(f, durSeconds) Then
                            durSeconds = Await GetMediaDurationSecondsAsync(f, libVLC)
                            If durSeconds > 0 Then cache.SetDuration(f, durSeconds)
                        End If
                        Dim dur = TimeSpan.FromSeconds(durSeconds)
                        If dur > TimeSpan.Zero Then fileDurations.Add((f, dur))
                    Next

                    ' Schedule the day as a repeating cycle, like a real cable
                    ' channel's daily lineup: a movie over an hour, then a block of
                    ' exactly 2 shows (drawn from the shared Shows pool, with 1-3
                    ' short filler clips scattered around them), then a movie of 2+
                    ' hours, then repeat. Movies still get an hour-aligned filler
                    ' break before they start so they kick off on the hour when
                    ' possible; the show block doesn't bother aligning since shows
                    ' aren't expected to start on the hour.
                    Dim longMovies = fileDurations.Where(Function(e) e.Duration > TimeSpan.FromHours(1)).OrderBy(Function(e) Guid.NewGuid()).ToList()
                    Dim veryLongMovies = fileDurations.Where(Function(e) e.Duration >= TimeSpan.FromHours(2)).OrderBy(Function(e) Guid.NewGuid()).ToList()
                    Dim anyLocal = fileDurations.OrderBy(Function(e) Guid.NewGuid()).ToList()
                    Dim longestShow = If(showsClips.Count > 0, New List(Of (Path As String, Duration As TimeSpan)) From {showsClips.OrderByDescending(Function(e) e.Duration).First()}, New List(Of (Path As String, Duration As TimeSpan))())

                    ' Fall back gracefully when a channel doesn't have movies of the
                    ' requested length: use whatever local movies it does have, and
                    ' only reach into the shared Shows pool as a last resort so a
                    ' channel with no local content of its own still airs something.
                    Dim movieAPool = If(longMovies.Count > 0, longMovies, If(anyLocal.Count > 0, anyLocal, longestShow))
                    Dim movieBPool = If(veryLongMovies.Count > 0, veryLongMovies, If(longMovies.Count > 0, longMovies, If(anyLocal.Count > 0, anyLocal, longestShow)))
                    Dim showsShuffled = showsClips.OrderBy(Function(e) Guid.NewGuid()).ToList()

                    Dim dayStart = DateTime.Today
                    Dim dayEnd = dayStart.AddDays(1)
                    Dim cursor = dayStart
                    Dim movieAIndex = 0
                    Dim movieBIndex = 0
                    Dim showIndex = 0
                    Dim cycles = 0
                    Const maxCyclesPerChannel = 200 ' guard against runaway loops on very short clips

                    If movieAPool.Count > 0 OrElse movieBPool.Count > 0 OrElse showsShuffled.Count > 0 Then
                        While cursor < dayEnd AndAlso cycles < maxCyclesPerChannel
                            ' movie over an hour
                            If movieAPool.Count > 0 Then
                                If ch.Programs.Count > 0 Then
                                    Dim preA = BuildFillerBreak(ch.Id, cursor, fillerClips, rnd)
                                    ch.Programs.AddRange(preA.Items)
                                    cursor = preA.NewCursor
                                End If
                                Dim a = movieAPool(movieAIndex Mod movieAPool.Count)
                                movieAIndex += 1
                                Dim aItem = New ProgramItem With {.ChannelId = ch.Id, .Title = System.IO.Path.GetFileNameWithoutExtension(a.Path), .Start = cursor, .EndTime = cursor.Add(a.Duration), .FilePath = a.Path, .FileName = System.IO.Path.GetFileName(a.Path)}
                                ch.Programs.Add(aItem)
                                cursor = aItem.EndTime
                            End If
                            If cursor >= dayEnd Then Exit While

                            ' 2 shows with 1-3 total filler clips scattered around them
                            If showsShuffled.Count > 0 Then
                                Dim block = BuildShowBlock(ch.Id, cursor, showsShuffled, showIndex, fillerClips, rnd, ch.Programs.Count > 0)
                                ch.Programs.AddRange(block.Items)
                                cursor = block.NewCursor
                                showIndex = block.NewShowIndex
                            End If
                            If cursor >= dayEnd Then Exit While

                            ' movie of 2+ hours - also gets an hour-aligned break first
                            If movieBPool.Count > 0 Then
                                If ch.Programs.Count > 0 Then
                                    Dim preB = BuildFillerBreak(ch.Id, cursor, fillerClips, rnd)
                                    ch.Programs.AddRange(preB.Items)
                                    cursor = preB.NewCursor
                                End If
                                Dim b = movieBPool(movieBIndex Mod movieBPool.Count)
                                movieBIndex += 1
                                Dim bItem = New ProgramItem With {.ChannelId = ch.Id, .Title = System.IO.Path.GetFileNameWithoutExtension(b.Path), .Start = cursor, .EndTime = cursor.Add(b.Duration), .FilePath = b.Path, .FileName = System.IO.Path.GetFileName(b.Path)}
                                ch.Programs.Add(bItem)
                                cursor = bItem.EndTime
                            End If

                            cycles += 1
                        End While
                    End If
                    result.Add(ch)
                    Try
                        onChannelScanned?.Invoke(ch)
                    Catch
                    End Try
                    If progress IsNot Nothing Then progress.Report(CInt(((i + 1) / CSng(directories.Length)) * 100))
                Next

                Return result
            End If

            ' flat folder (no subfolders): split all videos round-robin across synthetic
            ' channels so different channels air different movies at the same time,
            ' instead of dumping every movie into one long single-channel timeline.
            Const channelCount As Integer = 8
            Dim allFiles = Directory.GetFiles(rootPath, "*.*", SearchOption.TopDirectoryOnly).
                Where(Function(f) _videoExtensions.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant())).
                OrderBy(Function(f) f).ToArray()

            Dim channels = New List(Of Channel)()
            For c = 0 To channelCount - 1
                channels.Add(New Channel With {.Id = Guid.NewGuid().ToString(), .Name = "Channel " & (c + 1), .FolderPath = rootPath})
            Next
            Dim cursors = Enumerable.Repeat(DateTime.Today, channelCount).ToArray()

            For i = 0 To allFiles.Length - 1
                Dim f = allFiles(i)
                Dim channelIndex = i Mod channelCount
                Dim ch = channels(channelIndex)

                Dim durSeconds As Double = 0
                If Not cache.TryGet(f, durSeconds) Then
                    durSeconds = Await GetMediaDurationSecondsAsync(f, libVLC)
                    If durSeconds > 0 Then cache.SetDuration(f, durSeconds)
                End If
                Dim dur = TimeSpan.FromSeconds(durSeconds)
                If dur = TimeSpan.Zero Then Continue For

                Dim cursor = cursors(channelIndex)
                Dim pItem = New ProgramItem With {.ChannelId = ch.Id, .Title = System.IO.Path.GetFileNameWithoutExtension(f), .Start = cursor, .EndTime = cursor.Add(dur), .FilePath = f, .FileName = System.IO.Path.GetFileName(f)}
                ch.Programs.Add(pItem)
                cursors(channelIndex) = pItem.EndTime

                Dim brk = BuildFillerBreak(ch.Id, cursors(channelIndex), fillerClips, rnd)
                ch.Programs.AddRange(brk.Items)
                cursors(channelIndex) = brk.NewCursor

                If progress IsNot Nothing Then progress.Report(CInt(((i + 1) / CSng(allFiles.Length)) * 100))
            Next

            For Each ch In channels
                result.Add(ch)
                Try
                    onChannelScanned?.Invoke(ch)
                Catch
                End Try
            Next

            Return result
        End Function

        ''' Returns the first integer found anywhere in a folder name - "Channel 12" -> 12,
        ''' "Ch10 Tremors" -> 10 - or Integer.MaxValue if it has none, so numeric folders
        ''' sort in natural order (2, 10, 11, 12) instead of plain string order (10, 11, 12, 2).
        Private Shared Function ExtractChannelNumber(name As String) As Integer
            Dim m = System.Text.RegularExpressions.Regex.Match(name, "\d+")
            If m.Success Then
                Dim n As Integer
                If Integer.TryParse(m.Value, n) Then Return n
            End If
            Return Integer.MaxValue
        End Function

        ''' Loads every clip directly under `folder`, resolving/caching each one's
        ''' duration (via its own durations.json in that folder) the same way regular
        ''' per-channel programs are. Shared by the Filler and Shows root folders,
        ''' which both hold flat pools of clips reused across every channel.
        Private Shared Async Function LoadClipsFromFolderAsync(folder As String, libVLC As LibVLC) As Task(Of List(Of (Path As String, Duration As TimeSpan)))
            Dim result As New List(Of (Path As String, Duration As TimeSpan))()
            If String.IsNullOrEmpty(folder) OrElse Not Directory.Exists(folder) Then Return result

            Dim folderCache = New DurationCache(folder)
            Dim files = Directory.GetFiles(folder, "*.*", SearchOption.TopDirectoryOnly).
                Where(Function(f) _videoExtensions.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant())).ToArray()

            For Each f In files
                Dim durSeconds As Double = 0
                If Not folderCache.TryGet(f, durSeconds) Then
                    durSeconds = Await GetMediaDurationSecondsAsync(f, libVLC)
                    If durSeconds > 0 Then folderCache.SetDuration(f, durSeconds)
                End If
                Dim dur = TimeSpan.FromSeconds(durSeconds)
                If dur > TimeSpan.Zero Then result.Add((f, dur))
            Next
            Return result
        End Function

        ''' Filler clips (short commercial-break videos) so BuildFillerBreak has clip
        ''' lengths available up front to pick clips that best fill the gap before the
        ''' next show/movie.
        Private Shared Function LoadFillerClipsAsync(libVLC As LibVLC) As Task(Of List(Of (Path As String, Duration As TimeSpan)))
            Return LoadClipsFromFolderAsync(RootFolderResolver.GetFillerRootFolder(), libVLC)
        End Function

        ''' Short shows shared across every channel, mixed into each channel's own
        ''' local lineup so every channel - including ones with little or no local
        ''' content of their own - airs some programming.
        Private Shared Function LoadShowsClipsAsync(libVLC As LibVLC) As Task(Of List(Of (Path As String, Duration As TimeSpan)))
            Return LoadClipsFromFolderAsync(RootFolderResolver.GetShowsRootFolder(), libVLC)
        End Function

        ''' Builds a commercial break of 1-3 filler clips starting at `cursor`, trying
        ''' to land the following program's start time on the next top of the hour -
        ''' picking clips whose durations best add up to that gap - and snapping the
        ''' returned cursor to that hour if the break landed within 5 minutes of it.
        ''' If the gap can't be hit well (clips too long/short, or cursor is already
        ''' on the hour), the break still plays 1-3 clips and the schedule just
        ''' continues from wherever that leaves the cursor.
        Private Shared Function BuildFillerBreak(channelId As String, cursor As DateTime, fillerClips As List(Of (Path As String, Duration As TimeSpan)), rnd As Random) As (Items As List(Of ProgramItem), NewCursor As DateTime)
            Dim items As New List(Of ProgramItem)()
            If fillerClips Is Nothing OrElse fillerClips.Count = 0 Then Return (items, cursor)

            Dim nextHour As DateTime
            If cursor.Minute = 0 AndAlso cursor.Second = 0 AndAlso cursor.Millisecond = 0 Then
                nextHour = cursor
            Else
                nextHour = cursor.Date.AddHours(cursor.Hour + 1)
            End If

            Dim breakCount = rnd.Next(1, 4) ' 1 to 3 filler videos per break
            Dim runningCursor = cursor

            For slot = 1 To breakCount
                Dim slotsLeft = breakCount - slot + 1
                Dim remaining = nextHour - runningCursor
                If remaining < TimeSpan.Zero Then remaining = TimeSpan.Zero
                Dim targetForSlot = TimeSpan.FromSeconds(remaining.TotalSeconds / slotsLeft)

                ' pick from among the closest-fitting clips at random, so a break
                ' doesn't always reuse the exact same filler for a given gap size
                Dim candidates = fillerClips.OrderBy(Function(c) Math.Abs((c.Duration - targetForSlot).TotalSeconds)).Take(5).ToList()
                Dim pick = candidates(rnd.Next(candidates.Count))

                Dim item = New ProgramItem With {.ChannelId = channelId, .Title = System.IO.Path.GetFileNameWithoutExtension(pick.Path), .Start = runningCursor, .EndTime = runningCursor.Add(pick.Duration), .FilePath = pick.Path, .FileName = System.IO.Path.GetFileName(pick.Path), .IsFiller = True}
                items.Add(item)
                runningCursor = item.EndTime
            Next

            ' only snap forward to the hour (never rewind past clips we already scheduled)
            If runningCursor < nextHour AndAlso (nextHour - runningCursor) <= TimeSpan.FromMinutes(5) Then
                runningCursor = nextHour
            End If

            Return (items, runningCursor)
        End Function

        ''' Builds the "2 shows" block that sits between the hour-plus movie and the
        ''' 2-hour-plus movie in each channel's daily cycle: picks 2 shows from the
        ''' shared, per-channel-shuffled `showsPool` (cycling back to the start via
        ''' `showIndex` once it runs out, same as the movie pools), and scatters a
        ''' total of 1-3 filler clips across the gap before show 1 and the gap
        ''' between show 1 and show 2 - no hour-alignment here, since only movies are
        ''' expected to start on the hour. `allowLeadingBreak` is False when this
        ''' block is the very first thing scheduled for the channel that day, so the
        ''' day doesn't open with a commercial before any real programming has aired.
        Private Shared Function BuildShowBlock(channelId As String, cursor As DateTime, showsPool As List(Of (Path As String, Duration As TimeSpan)), showIndex As Integer, fillerClips As List(Of (Path As String, Duration As TimeSpan)), rnd As Random, allowLeadingBreak As Boolean) As (Items As List(Of ProgramItem), NewCursor As DateTime, NewShowIndex As Integer)
            Dim items As New List(Of ProgramItem)()
            Dim running = cursor
            If showsPool Is Nothing OrElse showsPool.Count = 0 Then Return (items, cursor, showIndex)

            Dim hasFillers = fillerClips IsNot Nothing AndAlso fillerClips.Count > 0
            Dim total = If(hasFillers, rnd.Next(1, 4), 0) ' 1 to 3 filler clips total for this whole block
            Dim n1 = If(allowLeadingBreak, rnd.Next(0, total + 1), 0)
            Dim n2 = total - n1

            Dim addFillers = Sub(count As Integer)
                                  For i = 1 To count
                                      Dim pick = fillerClips(rnd.Next(fillerClips.Count))
                                      Dim fItem = New ProgramItem With {.ChannelId = channelId, .Title = System.IO.Path.GetFileNameWithoutExtension(pick.Path), .Start = running, .EndTime = running.Add(pick.Duration), .FilePath = pick.Path, .FileName = System.IO.Path.GetFileName(pick.Path), .IsFiller = True}
                                      items.Add(fItem)
                                      running = fItem.EndTime
                                  Next
                              End Sub

            addFillers(n1)

            Dim idx = showIndex
            Dim s1 = showsPool(idx Mod showsPool.Count) : idx += 1
            Dim s1Item = New ProgramItem With {.ChannelId = channelId, .Title = System.IO.Path.GetFileNameWithoutExtension(s1.Path), .Start = running, .EndTime = running.Add(s1.Duration), .FilePath = s1.Path, .FileName = System.IO.Path.GetFileName(s1.Path)}
            items.Add(s1Item) : running = s1Item.EndTime

            addFillers(n2)

            Dim s2 = showsPool(idx Mod showsPool.Count) : idx += 1
            Dim s2Item = New ProgramItem With {.ChannelId = channelId, .Title = System.IO.Path.GetFileNameWithoutExtension(s2.Path), .Start = running, .EndTime = running.Add(s2.Duration), .FilePath = s2.Path, .FileName = System.IO.Path.GetFileName(s2.Path)}
            items.Add(s2Item) : running = s2Item.EndTime

            Return (items, running, idx)
        End Function

        Private Shared Async Function GetMediaDurationSecondsAsync(path As String, libVLC As LibVLC) As Task(Of Double)
            Try
                Using media = New LibVLCSharp.Shared.Media(libVLC, path, FromType.FromPath)
                    ' Media.Parse is asynchronous - it must be awaited, otherwise
                    ' media.Duration is read before parsing has actually finished
                    ' and always comes back as 0.
                    Await media.Parse(LibVLCSharp.Shared.MediaParseOptions.ParseLocal)
                    Dim ms = media.Duration
                    If ms <= 0 Then Return 0
                    Return ms / 1000.0
                End Using
            Catch
                Return 0
            End Try
        End Function

    End Class
End Namespace
