Imports System.IO
Imports System.Threading.Tasks
Imports System.Linq
Imports System.Windows.Threading
Imports LibVLCSharp.Shared
Imports System.Text.Json

Namespace TVMoviesChannelGuide
    Public Class FolderScanner
        Private Shared ReadOnly _videoExtensions As String() = {".mp4"}

        Public Shared Async Function ScanAsync(rootPath As String, dispatcher As Dispatcher, libVLC As LibVLC, progress As IProgress(Of Integer), onChannelScanned As Action(Of Channel), onProgramUpdated As Action(Of Channel, ProgramItem)) As Task(Of System.Collections.Generic.List(Of Channel))
            Dim result = New System.Collections.Generic.List(Of Channel)()
            If Not Directory.Exists(rootPath) Then Return result

            Dim cache = New DurationCache(rootPath)

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
                            ' generate thumbnail in background and notify when ready
                            Task.Run(Sub()
                                         Try
                                             Dim thumbFolder = System.IO.Path.Combine(rootPath, "Thumbnails")
                                             Dim thumb = ThumbnailService.GenerateThumbnailAsync(libVLC, f, thumbFolder, 320, 180).Result
                                             If Not String.IsNullOrWhiteSpace(thumb) Then
                                                 pItem.ThumbnailPath = thumb
                                                 Try
                                                     onProgramUpdated?.Invoke(ch, pItem)
                                                 Catch
                                                 End Try
                                             End If
                                         Catch
                                         End Try
                                     End Sub)
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
            Dim directories = Directory.GetDirectories(rootPath)
            Dim channelDirs = If(directories.Length = 0, New String() {rootPath}, directories)
            For i = 0 To channelDirs.Length - 1
                Dim dir = channelDirs(i)
                Dim ch = New Channel With {.Id = Guid.NewGuid().ToString(), .Name = System.IO.Path.GetFileName(dir), .FolderPath = dir}
                Dim files = Directory.GetFiles(dir, "*.*", SearchOption.TopDirectoryOnly).Where(Function(f) _videoExtensions.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant())).OrderBy(Function(f) f).ToArray()
                Dim cursor = DateTime.Today
                For Each f In files.Where(Function(p) System.IO.Path.GetExtension(p).ToLowerInvariant() = ".mp4")
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
                    Task.Run(Sub()
                                 Try
                                     Dim thumbFolder = System.IO.Path.Combine(rootPath, "Thumbnails")
                                     Dim thumb = ThumbnailService.GenerateThumbnailAsync(libVLC, f, thumbFolder, 320, 180).Result
                                     If Not String.IsNullOrWhiteSpace(thumb) Then
                                         pItem.ThumbnailPath = thumb
                                         Try
                                             onProgramUpdated?.Invoke(ch, pItem)
                                         Catch
                                         End Try
                                     End If
                                 Catch
                                 End Try
                             End Sub)
                Next
                result.Add(ch)
                Try
                    onChannelScanned?.Invoke(ch)
                Catch
                End Try
                If progress IsNot Nothing Then progress.Report(CInt(((i + 1) / CSng(channelDirs.Length)) * 100))
            Next

            Return result
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
End Namespace
