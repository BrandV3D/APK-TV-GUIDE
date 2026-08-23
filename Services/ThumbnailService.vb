Imports LibVLCSharp.Shared
Imports System.IO
Imports System.Threading.Tasks

Public Class ThumbnailService
    ''' <summary>
    ''' Generate a thumbnail image for a video file using LibVLC snapshot. Returns the thumbnail path or Nothing on failure.
    ''' </summary>
    Public Shared Async Function GenerateThumbnailAsync(libVLC As LibVLC, videoPath As String, outputFolder As String, width As Integer, height As Integer) As Task(Of String)
        Try
            If Not File.Exists(videoPath) Then Return Nothing
            If Not Directory.Exists(outputFolder) Then Directory.CreateDirectory(outputFolder)

            Dim fileName = Path.GetFileNameWithoutExtension(videoPath)
            Dim thumbPath = Path.Combine(outputFolder, fileName & "_thumb.jpg")

            ' Use a MediaPlayer to play briefly and take a snapshot
            Return Await Task.Run(Function()
                                      Try
                                          Using media = New Media(libVLC, videoPath, FromType.FromPath)
                                              Using player = New MediaPlayer(libVLC)
                                                  player.Media = media
                                                  player.Volume = 0
                                                  player.Play()
                                                  ' wait a moment for video to start
                                                  Threading.Thread.Sleep(700)
                                                  ' take snapshot (0 denotes the first video output)
                                                  Dim ok = player.TakeSnapshot(0, width, height, thumbPath)
                                                  Try
                                                      player.Stop()
                                                  Catch
                                                  End Try
                                                  If ok Then
                                                      Return thumbPath
                                                  Else
                                                      Return Nothing
                                                  End If
                                              End Using
                                          End Using
                                      Catch
                                          Return Nothing
                                      End Try
                                  End Function)
        Catch
            Return Nothing
        End Try
    End Function
End Class
