Imports System.IO
Imports System.Threading.Tasks
Imports System.Linq
Imports System.Windows.Threading
Imports System.Windows.Media
Imports LibVLCSharp.Shared

Public Class FolderScanner
    Private Shared ReadOnly _videoExtensions As String() = {".mp4", ".mkv", ".avi", ".mov", ".wmv", ".m4v"}

    ''' <summary>
    ''' Scan the root folder. Each immediate subfolder becomes a channel. Files are ordered by name and scheduled sequentially starting at midnight today.
    ''' </summary>
    Public Shared Async Function ScanAsync(rootPath As String, dispatcher As Dispatcher, libVLC As LibVLC) As Task(Of System.Collections.Generic.List(Of Channel))
        Dim result = New System.Collections.Generic.List(Of Channel)()
        If Not Directory.Exists(rootPath) Then Return result

        Dim directories = Directory.GetDirectories(rootPath)
        Dim channelDirs As String()
        If directories.Length = 0 Then
            ' Treat root as single channel
            channelDirs = New String() {rootPath}
        Else
            channelDirs = directories
        End If

        For i = 0 To channelDirs.Length - 1
            Dim dir = channelDirs(i)
            Dim ch = New Channel With {
                .Id = Guid.NewGuid().ToString(),
                .Name = Path.GetFileName(dir),
                .FolderPath = dir
            }

            ' enumerate video files recursively under this folder
                Dim files = Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories) _
                        .Where(Function(f) _videoExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())) _
                        .OrderBy(Function(f) f).ToArray()

            Dim cursor = DateTime.Today
                For Each f In files
                    Dim durSeconds As Double = 0
                    Dim cache = New DurationCache(rootPath)
                    If Not cache.TryGet(f, durSeconds) Then
                        durSeconds = Await GetMediaDurationSecondsAsync(f, libVLC)
                        If durSeconds > 0 Then
                            cache.SetDuration(f, durSeconds)
                        End If
                    End If
                    Dim dur = TimeSpan.FromSeconds(durSeconds)
                If dur = TimeSpan.Zero Then
                    ' Try to skip files with unknown duration
                    Continue For
                End If

                Dim p = New ProgramItem With {
                    .ChannelId = ch.Id,
                    .Title = Path.GetFileNameWithoutExtension(f),
                    .Start = cursor,
                    .EndTime = cursor.Add(dur),
                    .FilePath = f
                }
                ch.Programs.Add(p)
                cursor = p.EndTime
            Next

            result.Add(ch)
        Next

        Return result
    End Function

    Private Shared Function GetMediaDurationAsync(filePath As String, dispatcher As Dispatcher) As Task(Of TimeSpan)
        Dim tcs = New TaskCompletionSource(Of TimeSpan)()

        dispatcher.Invoke(Sub()
                              Try
                                  Dim player = New System.Windows.Media.MediaPlayer()
                                  AddHandler player.MediaOpened, Sub(s, e)
                                                                     Dim d = player.NaturalDuration
                                                                     If d.HasTimeSpan Then
                                                                         tcs.TrySetResult(d.TimeSpan)
                                                                     Else
                                                                         tcs.TrySetResult(TimeSpan.Zero)
                                                                     End If
                                                                     player.Close()
                                                                 End Sub
                                  ' MediaPlayer does not have a MediaFailed event; use LoadedBehavior approach instead
                                  player.Open(New Uri(filePath))
                              Catch ex As Exception
                                  tcs.TrySetResult(TimeSpan.Zero)
                              End Try
                          End Sub)

        Return tcs.Task
    End Function

    Private Shared Function GetMediaDurationSecondsAsync(path As String, libVLC As LibVLC) As Task(Of Double)
        Return Task.Run(Function()
                            Try
                                Using media = New LibVLCSharp.Shared.Media(libVLC, path, FromType.FromPath)
                                    media.Parse(LibVLCSharp.Shared.MediaParseOptions.ParseLocal)
                                    Dim ms = media.Duration
                                    If ms <= 0 Then Return 0
                                    Return ms / 1000.0
                                End Using
                            Catch
                                Return 0
                            End Try
                        End Function)
    End Function
End Class
