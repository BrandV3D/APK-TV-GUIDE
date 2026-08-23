Imports System.IO
Imports System.Text.Json
Imports Microsoft.Win32
Imports System.Windows.Forms

Public Class ChannelConfig
    Public Property Name As String
    Public Property Folder As String
    Public Property Files As String()
    Public Property Logo As String
End Class

Public Class AvailableFile
    Public Property FullPath As String
    Public Property DisplayPath As String
    Public Property IsChecked As Boolean
End Class

Public Class ChannelEditor
    Private _root As String
    Private _channels As New System.Collections.ObjectModel.ObservableCollection(Of ChannelConfig)

    Public Sub New(rootFolder As String)
        InitializeComponent()
        _root = rootFolder
        LstEditorChannels.ItemsSource = _channels

        AddHandler BtnAddChannel.Click, AddressOf BtnAddChannel_Click
        AddHandler BtnRemoveChannel.Click, AddressOf BtnRemoveChannel_Click
        AddHandler BtnBrowseFolder.Click, AddressOf BtnBrowseFolder_Click
        AddHandler BtnAddFiles.Click, AddressOf BtnAddFiles_Click
        AddHandler BtnRemoveFile.Click, AddressOf BtnRemoveFile_Click
        AddHandler BtnBrowseLogo.Click, AddressOf BtnBrowseLogo_Click
        AddHandler BtnMoveUp.Click, AddressOf BtnMoveUp_Click
        AddHandler BtnMoveDown.Click, AddressOf BtnMoveDown_Click
        AddHandler BtnLoadFolderFiles.Click, AddressOf BtnLoadFolderFiles_Click
        AddHandler TxtChannelFolder.LostFocus, AddressOf TxtChannelFolder_LostFocus
        AddHandler TxtChannelFolder.KeyDown, AddressOf TxtChannelFolder_KeyDown
        AddHandler BtnSaveChannels.Click, AddressOf BtnSaveChannels_Click
        AddHandler BtnCloseEditor.Click, AddressOf BtnCloseEditor_Click
        AddHandler LstEditorChannels.SelectionChanged, AddressOf LstEditorChannels_SelectionChanged
        AddHandler BtnHome.Click, AddressOf BtnHome_Click

        LoadExisting()
    End Sub

    Private Sub BtnHome_Click(sender As Object, e As RoutedEventArgs)
        Try
            Dim splash = New SplashWindow()
            CloseDuplicateWindows(splash)
            splash.Show()
        Catch
        End Try
        Me.Close()
    End Sub

    ' Drag-drop reordering for LstFiles
    Private _dragStartIndex As Integer = -1

    Private Sub LstFiles_PreviewMouseLeftButtonDown(sender As Object, e As System.Windows.Input.MouseButtonEventArgs)
        Try
            Dim lb = TryCast(sender, System.Windows.Controls.ListBox)
            If lb Is Nothing Then Return
            ' simply record selected index to start drag
            _dragStartIndex = lb.SelectedIndex
            ' refreshed
        Catch
        End Try
    End Sub

    Private Sub LstFiles_MouseMove(sender As Object, e As System.Windows.Input.MouseEventArgs)
        Try
            If e.LeftButton = System.Windows.Input.MouseButtonState.Pressed AndAlso _dragStartIndex >= 0 Then
                Dim lb = TryCast(sender, System.Windows.Controls.ListBox)
                If lb Is Nothing Then Return
                ' begin drag operation (no-op change)
                Dim data = lb.Items(_dragStartIndex)
                Dim dragData = New DataObject("ChannelFile", data)
                DragDrop.DoDragDrop(lb, dragData, DragDropEffects.Move)
            End If
        Catch
        End Try
    End Sub

    Private Sub LstFiles_Drop(sender As Object, e As System.Windows.DragEventArgs)
        Try
            Dim lb = TryCast(sender, System.Windows.Controls.ListBox)
            If lb Is Nothing Then Return
            If Not e.Data.GetDataPresent("ChannelFile") Then Return
            Dim data = e.Data.GetData("ChannelFile")
            Dim cfg = TryCast(LstEditorChannels.SelectedItem, ChannelConfig)
            If cfg Is Nothing Then Return
            Dim list As New List(Of String)(If(cfg.Files, New String() {}))
            Dim item = TryCast(data, String)
            If item Is Nothing Then Return
            Dim pt = e.GetPosition(lb)
            Dim index = lb.SelectedIndex
            ' find target index based on mouse position
            Dim target As Integer = -1
            For i = 0 To lb.Items.Count - 1
                Dim container = TryCast(lb.ItemContainerGenerator.ContainerFromIndex(i), System.Windows.Controls.ListBoxItem)
                If container Is Nothing Then Continue For
                Dim bounds = container.TransformToAncestor(lb).TransformBounds(New System.Windows.Rect(New System.Windows.Point(0, 0), New System.Windows.Size(container.ActualWidth, container.ActualHeight)))
                If pt.Y >= bounds.Top AndAlso pt.Y <= bounds.Bottom Then
                    target = i
                    Exit For
                End If
            Next
            If target = -1 Then target = lb.Items.Count - 1
            If target < 0 Then target = lb.Items.Count - 1
            ' remove original
            Dim origIdx = list.IndexOf(item)
            If origIdx >= 0 Then
                list.RemoveAt(origIdx)
                If target > origIdx Then target -= 1
            End If
            list.Insert(Math.Max(0, target), item)
            cfg.Files = list.ToArray()
            LstFiles.ItemsSource = Nothing
            LstFiles.ItemsSource = cfg.Files
        Catch
        Finally
            _dragStartIndex = -1
        End Try
    End Sub

    Private Sub BtnLoadFolderFiles_Click(sender As Object, e As RoutedEventArgs)
        Try
            Dim folder = TxtChannelFolder.Text
            If String.IsNullOrWhiteSpace(folder) Then
                MessageBox.Show("Set a channel folder first.")
                Return
            End If
            Dim full = If(System.IO.Path.IsPathRooted(folder), folder, System.IO.Path.Combine(_root, folder))
            If Not Directory.Exists(full) Then
                MessageBox.Show("Folder does not exist: " & full)
                Return
            End If
            Dim files = Directory.GetFiles(full, "*.mp4", SearchOption.TopDirectoryOnly).OrderBy(Function(f) f).ToArray()
            Dim avail = New System.Collections.ObjectModel.ObservableCollection(Of AvailableFile)()
            For Each f In files
                Dim disp = If(f.StartsWith(_root), f.Substring(_root.Length).TrimStart(System.IO.Path.DirectorySeparatorChar), f)
                avail.Add(New AvailableFile With {.FullPath = f, .DisplayPath = disp, .IsChecked = False})
            Next
            LstAvailableFiles.ItemsSource = avail
        Catch ex As Exception
            MessageBox.Show("Error loading files: " & ex.Message)
        End Try
    End Sub

    Private Sub TxtChannelFolder_LostFocus(sender As Object, e As RoutedEventArgs)
        Try
            BtnLoadFolderFiles_Click(Nothing, Nothing)
        Catch
        End Try
    End Sub

    Private Sub TxtChannelFolder_KeyDown(sender As Object, e As System.Windows.Input.KeyEventArgs)
        If e.Key = System.Windows.Input.Key.Enter Then
            BtnLoadFolderFiles_Click(Nothing, Nothing)
        End If
    End Sub

    Private Sub LoadExisting()
        If String.IsNullOrWhiteSpace(_root) Then Return
        Dim configPath As String = Path.Combine(_root, "channels.json")
        If File.Exists(configPath) Then
            Try
                Dim txt = File.ReadAllText(configPath)
                Dim arr = JsonSerializer.Deserialize(Of ChannelConfig())(txt)
                If arr IsNot Nothing Then
                    For Each c In arr
                        _channels.Add(c)
                    Next
                End If
            Catch
            End Try
        Else
            ' load subfolders as default channels
            Try
                For Each d In Directory.GetDirectories(_root)
                    _channels.Add(New ChannelConfig With {.Name = Path.GetFileName(d), .Folder = Path.GetFileName(d), .Files = New String() {}, .Logo = Nothing})
                Next
            Catch
            End Try
        End If
    End Sub

    Private Sub LstEditorChannels_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)
        Dim cfg = TryCast(LstEditorChannels.SelectedItem, ChannelConfig)
        If cfg Is Nothing Then
            TxtChannelName.Text = ""
            TxtChannelFolder.Text = ""
            TxtLogoPath.Text = ""
            ImgPreviewLogo.Source = Nothing
            LstFiles.ItemsSource = Nothing
            Return
        End If
        TxtChannelName.Text = cfg.Name
        TxtChannelFolder.Text = cfg.Folder
        TxtLogoPath.Text = cfg.Logo
        If Not String.IsNullOrWhiteSpace(cfg.Logo) Then
            Try
                Dim logoPath = If(Path.IsPathRooted(cfg.Logo), cfg.Logo, Path.Combine(_root, cfg.Logo))
                If File.Exists(logoPath) Then ImgPreviewLogo.Source = New System.Windows.Media.Imaging.BitmapImage(New Uri(logoPath))
            Catch
            End Try
        Else
            ImgPreviewLogo.Source = Nothing
        End If
        LstFiles.ItemsSource = cfg.Files
    End Sub

    Private Sub BtnAddChannel_Click(sender As Object, e As RoutedEventArgs)
        Dim c = New ChannelConfig With {.Name = "New Channel", .Folder = "", .Files = New String() {}}
        _channels.Add(c)
        LstEditorChannels.SelectedItem = c
    End Sub

    Private Sub BtnRemoveChannel_Click(sender As Object, e As RoutedEventArgs)
        Dim cfg = TryCast(LstEditorChannels.SelectedItem, ChannelConfig)
        If cfg Is Nothing Then Return
        _channels.Remove(cfg)
    End Sub

    Private Sub BtnBrowseFolder_Click(sender As Object, e As RoutedEventArgs)
        Using dlg = New System.Windows.Forms.FolderBrowserDialog()
            dlg.ShowNewFolderButton = False
            If dlg.ShowDialog() = System.Windows.Forms.DialogResult.OK Then
                TxtChannelFolder.Text = If(dlg.SelectedPath.StartsWith(_root), dlg.SelectedPath.Substring(_root.Length).TrimStart(Path.DirectorySeparatorChar), dlg.SelectedPath)
            End If
        End Using
    End Sub

    Private Sub BtnAddFiles_Click(sender As Object, e As RoutedEventArgs)
        ' If available files list has selection, add those; otherwise fall back to file picker
        Dim cfg = TryCast(LstEditorChannels.SelectedItem, ChannelConfig)
        If cfg Is Nothing Then Return

        Dim added As New List(Of String)(If(cfg.Files, New String() {}))
        Dim anyAdded As Boolean = False

        If LstAvailableFiles.ItemsSource IsNot Nothing Then
            For Each item In LstAvailableFiles.ItemsSource
                Dim af = TryCast(item, AvailableFile)
                If af IsNot Nothing AndAlso af.IsChecked Then
                    Dim rel = If(af.FullPath.StartsWith(_root), af.FullPath.Substring(_root.Length).TrimStart(Path.DirectorySeparatorChar), af.FullPath)
                    added.Add(rel)
                    anyAdded = True
                End If
            Next
        End If

        If Not anyAdded Then
            Dim ofd As New Microsoft.Win32.OpenFileDialog()
            ofd.Multiselect = True
            ofd.Filter = "MP4 files|*.mp4|All files|*.*"
            If ofd.ShowDialog() = True Then
                For Each f In ofd.FileNames
                    Dim rel = If(f.StartsWith(_root), f.Substring(_root.Length).TrimStart(Path.DirectorySeparatorChar), f)
                    added.Add(rel)
                Next
            End If
        End If

        cfg.Files = added.ToArray()
        LstFiles.ItemsSource = Nothing
        LstFiles.ItemsSource = cfg.Files
        UpdateFileListVisuals(cfg)
    End Sub

    Private Sub BtnBrowseLogo_Click(sender As Object, e As RoutedEventArgs)
        Dim ofd As New Microsoft.Win32.OpenFileDialog()
        ofd.Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp|All files|*.*"
        If ofd.ShowDialog() = True Then
            Dim cfg = TryCast(LstEditorChannels.SelectedItem, ChannelConfig)
            If cfg Is Nothing Then Return
            Dim rel = If(ofd.FileName.StartsWith(_root), ofd.FileName.Substring(_root.Length).TrimStart(Path.DirectorySeparatorChar), ofd.FileName)
            cfg.Logo = rel
            TxtLogoPath.Text = rel
            Try
                ImgPreviewLogo.Source = New System.Windows.Media.Imaging.BitmapImage(New Uri(ofd.FileName))
            Catch
            End Try
        End If
    End Sub

    Private Sub UpdateFileListVisuals(cfg As ChannelConfig)
        Try
            If cfg Is Nothing OrElse cfg.Files Is Nothing Then Return
            ' Refresh listbox items and color missing files red
            For i = 0 To LstFiles.Items.Count - 1
                Dim item = LstFiles.ItemContainerGenerator.ContainerFromIndex(i)
                If item Is Nothing Then Continue For
                Dim lbi = TryCast(item, System.Windows.Controls.ListBoxItem)
                If lbi Is Nothing Then Continue For
                Dim fp = TryCast(LstFiles.Items(i), String)
                Dim full = If(Path.IsPathRooted(fp), fp, Path.Combine(_root, fp))
                If Not File.Exists(full) Then
                    lbi.Foreground = New System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.IndianRed)
                Else
                    lbi.Foreground = New System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White)
                End If
            Next
        Catch
        End Try
    End Sub

    Private Sub BtnMoveUp_Click(sender As Object, e As RoutedEventArgs)
        Dim cfg = TryCast(LstEditorChannels.SelectedItem, ChannelConfig)
        If cfg Is Nothing OrElse cfg.Files Is Nothing Then Return
        Dim sel = TryCast(LstFiles.SelectedItem, String)
        If sel Is Nothing Then Return
        Dim idx = Array.IndexOf(cfg.Files, sel)
        If idx > 0 Then
            Dim list As New List(Of String)(cfg.Files)
            list.RemoveAt(idx)
            list.Insert(idx - 1, sel)
            cfg.Files = list.ToArray()
            LstFiles.ItemsSource = Nothing
            LstFiles.ItemsSource = cfg.Files
            LstFiles.SelectedIndex = idx - 1
        End If
    End Sub

    Private Sub BtnMoveDown_Click(sender As Object, e As RoutedEventArgs)
        Dim cfg = TryCast(LstEditorChannels.SelectedItem, ChannelConfig)
        If cfg Is Nothing OrElse cfg.Files Is Nothing Then Return
        Dim sel = TryCast(LstFiles.SelectedItem, String)
        If sel Is Nothing Then Return
        Dim idx = Array.IndexOf(cfg.Files, sel)
        If idx >= 0 AndAlso idx < cfg.Files.Length - 1 Then
            Dim list As New List(Of String)(cfg.Files)
            list.RemoveAt(idx)
            list.Insert(idx + 1, sel)
            cfg.Files = list.ToArray()
            LstFiles.ItemsSource = Nothing
            LstFiles.ItemsSource = cfg.Files
            LstFiles.SelectedIndex = idx + 1
        End If
    End Sub

    Private Sub BtnRemoveFile_Click(sender As Object, e As RoutedEventArgs)
        Dim cfg = TryCast(LstEditorChannels.SelectedItem, ChannelConfig)
        If cfg Is Nothing Then Return
        Dim sel = TryCast(LstFiles.SelectedItem, String)
        If sel Is Nothing Then Return
        Dim list As New List(Of String)(cfg.Files)
        list.Remove(sel)
        cfg.Files = list.ToArray()
        LstFiles.ItemsSource = Nothing
        LstFiles.ItemsSource = cfg.Files
    End Sub

    Private Sub BtnSaveChannels_Click(sender As Object, e As RoutedEventArgs)
        ' flush edited fields into selected config
        Dim sel = TryCast(LstEditorChannels.SelectedItem, ChannelConfig)
        If sel IsNot Nothing Then
            sel.Name = TxtChannelName.Text
            sel.Folder = TxtChannelFolder.Text
            sel.Logo = TxtLogoPath.Text
        End If

        Try
            Dim out = _channels.Select(Function(c)
                                           Return New ChannelConfig With {.Name = c.Name, .Folder = If(String.IsNullOrWhiteSpace(c.Folder), Nothing, c.Folder), .Files = If(c.Files IsNot Nothing AndAlso c.Files.Length > 0, c.Files, Nothing)}
                                       End Function).ToArray()
            Dim configPath As String = Path.Combine(_root, "channels.json")
            Dim json = JsonSerializer.Serialize(out, New JsonSerializerOptions With {.WriteIndented = True})
            File.WriteAllText(configPath, json)
            MessageBox.Show("Channels saved.")
        Catch ex As Exception
            MessageBox.Show("Error saving channels: " & ex.Message)
        End Try
    End Sub

    Private Sub BtnCloseEditor_Click(sender As Object, e As RoutedEventArgs)
        Me.Close()
    End Sub
End Class
