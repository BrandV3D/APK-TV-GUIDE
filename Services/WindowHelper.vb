Imports System.Linq
Imports System.Windows

''' Keeps navigation single-instance: whenever a new top-level window is opened,
''' any other already-open window of that same type is closed so instances don't
''' pile up as the user bounces between screens (Home button, Player, etc).
Public Module WindowHelper
    Public Sub CloseDuplicateWindows(newWindow As Window)
        Try
            For Each w In Application.Current.Windows.OfType(Of Window)().ToList()
                If w IsNot newWindow AndAlso w.GetType() Is newWindow.GetType() Then
                    w.Close()
                End If
            Next
        Catch
        End Try
    End Sub
End Module
