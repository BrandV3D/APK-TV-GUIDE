Imports System.IO
Imports System.Text.Json

Public Class DurationCache
    Private ReadOnly _cachePath As String
    Private _map As Dictionary(Of String, Double)

    Public Sub New(rootFolder As String)
        _cachePath = Path.Combine(rootFolder, "durations.json")
        Load()
    End Sub

    Private Sub Load()
        If File.Exists(_cachePath) Then
            Try
                Dim txt = File.ReadAllText(_cachePath)
                _map = JsonSerializer.Deserialize(Of Dictionary(Of String, Double))(txt)
            Catch
                _map = New Dictionary(Of String, Double)(StringComparer.OrdinalIgnoreCase)
            End Try
        Else
            _map = New Dictionary(Of String, Double)(StringComparer.OrdinalIgnoreCase)
        End If
    End Sub

    Public Function TryGet(path As String, ByRef seconds As Double) As Boolean
        Return _map.TryGetValue(path, seconds)
    End Function

    Public Sub SetDuration(path As String, seconds As Double)
        _map(path) = seconds
        Save()
    End Sub

    Private Sub Save()
        Try
            Dim txt = JsonSerializer.Serialize(_map)
            File.WriteAllText(_cachePath, txt)
        Catch
            ' ignore
        End Try
    End Sub
End Class
