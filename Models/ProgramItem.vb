Public Class ProgramItem
    Public Property ChannelId As String
    Public Property Title As String
    Public Property Start As DateTime
    Public Property EndTime As DateTime
    Public Property FilePath As String
    Public Property FileName As String
    Public Property ThumbnailPath As String

    Public ReadOnly Property Duration As TimeSpan
        Get
            Return EndTime.Subtract(Start)
        End Get
    End Property

    Public Overrides Function ToString() As String
        Return $"{Start:HH:mm} - {Title}"
    End Function
End Class
