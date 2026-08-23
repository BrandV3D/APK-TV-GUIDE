Imports System.IO
Imports System.Text.Json

Public Class UserSettings
    Public Property LastRootFolder As String
    Public Property ScheduleStart As DateTime
    Public Property RulerStart As DateTime
    Public Property Ruler24Mode As Boolean

    Private Shared Function GetSettingsPath() As String
        Dim dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TVMoviesChannelGuide")
        If Not Directory.Exists(dir) Then Directory.CreateDirectory(dir)
        Return Path.Combine(dir, "user_settings.json")
    End Function

    Public Shared Function Load() As UserSettings
        Try
            Dim path = GetSettingsPath()
            If File.Exists(path) Then
                Dim txt = File.ReadAllText(path)
                Dim cfg = JsonSerializer.Deserialize(Of UserSettings)(txt)
                If cfg IsNot Nothing Then Return cfg
            End If
        Catch
        End Try
        Return New UserSettings With {.ScheduleStart = DateTime.Today, .RulerStart = DateTime.Now, .Ruler24Mode = False}
    End Function

    Public Sub Save()
        Try
            Dim path = GetSettingsPath()
            Dim txt = JsonSerializer.Serialize(Me)
            File.WriteAllText(path, txt)
        Catch
        End Try
    End Sub
End Class
