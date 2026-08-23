Imports System.Windows

Public Class ChannelsGrid
    Public Sub New()
        InitializeComponent()
        AddHandler Me.Loaded, AddressOf ChannelsGrid_Loaded
    End Sub

    Private Sub ChannelsGrid_Loaded(sender As Object, e As RoutedEventArgs)
        Try
            WrapChannels.Children.Clear()
            For i = 1 To 50
                Dim btn = New Button With {
                    .Content = $"Channel {i}",
                    .Width = 260,
                    .Height = 140,
                    .Margin = New Thickness(8),
                    .FontSize = 20,
                    .FontWeight = FontWeights.Bold
                }
                AddHandler btn.Click, AddressOf ChannelTile_Click
                WrapChannels.Children.Add(btn)
            Next
        Catch
        End Try
    End Sub

    Private Sub ChannelTile_Click(sender As Object, e As RoutedEventArgs)
        Try
            Dim btn = TryCast(sender, Button)
            If btn Is Nothing Then Return
            Dim txt = btn.Content.ToString()
            ' extract channel number
            Dim parts = txt.Split(" "c)
            Dim num = 0
            If parts.Length > 1 AndAlso Integer.TryParse(parts(1), num) Then
                ' find main window and select channel
                Dim main = Application.Current.Windows.OfType(Of MainWindow)().FirstOrDefault()
                If main IsNot Nothing Then
                    If main.Channels IsNot Nothing AndAlso main.Channels.Count >= num Then
                        main.SelectChannelByIndex(num - 1)
                    End If
                End If
            End If
        Catch
        End Try
        Me.Close()
    End Sub
End Class
