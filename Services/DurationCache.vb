Imports System.IO
Imports System.Text.Json

Public Class DurationCache
    Private ReadOnly _cachePath As String
    Private _map As Dictionary(Of String, Double)
    Private _meta As Dictionary(Of String, DateTime)

    Public Sub New(rootFolder As String)
        _cachePath = Path.Combine(rootFolder, "durations.json")
        Load()
    End Sub

    Private Sub Load()
        If File.Exists(_cachePath) Then
            Try
                Dim txt = File.ReadAllText(_cachePath)
                Dim container = JsonSerializer.Deserialize(Of Dictionary(Of String, Object))(txt)
                _map = New Dictionary(Of String, Double)(StringComparer.OrdinalIgnoreCase)
                _meta = New Dictionary(Of String, DateTime)(StringComparer.OrdinalIgnoreCase)
                If container IsNot Nothing Then
                    For Each kv In container
                        Try
                            Dim obj = kv.Value
                            ' container values expected to be an object with duration and lastWrite ticks
                            Dim el = JsonSerializer.Deserialize(Of Dictionary(Of String, JsonElement))(JsonSerializer.Serialize(obj))
                            If el.ContainsKey("duration") Then _map(kv.Key) = el("duration").GetDouble()
                            If el.ContainsKey("lastWriteUtc") Then _meta(kv.Key) = DateTime.FromFileTimeUtc(el("lastWriteUtc").GetInt64())
                        Catch
                        End Try
                    Next
                End If
            Catch
                _map = New Dictionary(Of String, Double)(StringComparer.OrdinalIgnoreCase)
                _meta = New Dictionary(Of String, DateTime)(StringComparer.OrdinalIgnoreCase)
            End Try
        Else
            _map = New Dictionary(Of String, Double)(StringComparer.OrdinalIgnoreCase)
            _meta = New Dictionary(Of String, DateTime)(StringComparer.OrdinalIgnoreCase)
        End If
    End Sub

    Public Function TryGet(path As String, ByRef seconds As Double) As Boolean
        Dim found As Double = 0
        If _map.TryGetValue(path, found) Then
            ' validate timestamp
            Try
                Dim fi = New FileInfo(path)
                Dim current = fi.LastWriteTimeUtc
                Dim cached As DateTime = DateTime.MinValue
                If _meta.TryGetValue(path, cached) Then
                    If cached = current Then
                        seconds = found
                        Return True
                    Else
                        Return False
                    End If
                Else
                    Return False
                End If
            Catch
                Return False
            End Try
        End If
        Return False
    End Function

    Public Sub SetDuration(path As String, seconds As Double)
        _map(path) = seconds
        Try
            Dim fi = New FileInfo(path)
            _meta(path) = fi.LastWriteTimeUtc
        Catch
        End Try
        Save()
    End Sub

    Private Sub Save()
        Try
            Dim container = New Dictionary(Of String, Object)()
            For Each kv In _map
                Dim metaVal As Long = 0
                Dim last As DateTime = DateTime.MinValue
                If _meta.ContainsKey(kv.Key) Then last = _meta(kv.Key)
                If last <> DateTime.MinValue Then metaVal = last.ToFileTimeUtc()
                container(kv.Key) = New With {Key .duration = kv.Value, Key .lastWriteUtc = metaVal}
            Next
            Dim txt = JsonSerializer.Serialize(container)
            File.WriteAllText(_cachePath, txt)
        Catch
            ' ignore
        End Try
    End Sub
End Class
