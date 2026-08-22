Public Class Channel
    Public Property Id As String
    Public Property Name As String
    Public Property FolderPath As String
    Public Property Programs As System.Collections.Generic.List(Of ProgramItem)

    Public Sub New()
        Programs = New System.Collections.Generic.List(Of ProgramItem)()
    End Sub
End Class
