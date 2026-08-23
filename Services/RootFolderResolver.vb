Imports System.IO

Public Module RootFolderResolver

    Private Const LinkFileName As String = "LinkToRoot.txt"

    ''' Reads LinkToRoot.txt next to the running exe - the file Instructions.md tells
    ''' users to edit after publishing so the app knows where their "TV GUIDE" folder
    ''' lives - and returns the folder it points to, or Nothing if the file is
    ''' missing, empty, or names a folder that doesn't exist.
    Public Function GetLinkedRootFolder() As String
        Try
            Dim linkPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, LinkFileName)
            If Not File.Exists(linkPath) Then Return Nothing
            For Each line In File.ReadAllLines(linkPath)
                Dim candidate = line.Trim().Trim(""""c)
                If Not String.IsNullOrWhiteSpace(candidate) AndAlso Directory.Exists(candidate) Then
                    Return candidate
                End If
            Next
        Catch
        End Try
        Return Nothing
    End Function

    ''' Every publish scaffolds a "TV GUIDE" folder tree right next to the exe (see
    ''' the CreateTvGuidePublishFolders target in the .vbproj) - this returns that
    ''' sibling folder's absolute path if it exists, so the app has a working
    ''' library out of the box with no LinkToRoot.txt edit required.
    Public Function GetPublishedRootFolder() As String
        Try
            Dim candidate = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TV GUIDE")
            If Directory.Exists(candidate) Then Return candidate
        Catch
        End Try
        Return Nothing
    End Function

    ''' Fixed, install-independent fallback: a "TV GUIDE" folder directly under
    ''' C:\TVGUIDE_SDK\TVGUIDEROOT. Useful when the app is launched from somewhere
    ''' other than its own publish folder (e.g. a shortcut) but the library was
    ''' set up at this well-known location.
    Public Function GetFixedRootFolder() As String
        Try
            Dim candidate = Path.Combine("C:\TVGUIDE_SDK\TVGUIDEROOT", "TV GUIDE")
            If Directory.Exists(candidate) Then Return candidate
        Catch
        End Try
        Return Nothing
    End Function

    ''' Fixed folder of short filler clips played during commercial breaks between
    ''' scheduled shows/movies. Returns Nothing if it doesn't exist so callers can
    ''' just skip filler breaks entirely.
    Public Function GetFillerRootFolder() As String
        Try
            Dim candidate = "C:\TVGUIDE_SDK\TVGUIDEROOT\Filler"
            If Directory.Exists(candidate) Then Return candidate
        Catch
        End Try
        Return Nothing
    End Function

    ''' Fixed folder of short shows shared across every channel, mixed in alongside
    ''' each channel's own local movies/shows when building its daily schedule.
    ''' Returns Nothing if it doesn't exist so callers can just skip it.
    Public Function GetShowsRootFolder() As String
        Try
            Dim candidate = "C:\TVGUIDE_SDK\TVGUIDEROOT\Shows"
            If Directory.Exists(candidate) Then Return candidate
        Catch
        End Try
        Return Nothing
    End Function

    ''' Resolves the channel library root automatically, in priority order:
    ''' 1. LinkToRoot.txt, if it names a real folder (explicit override, e.g. a
    '''    different drive with more storage)
    ''' 2. The "TV GUIDE" folder published next to the exe
    ''' 3. C:\TVGUIDE_SDK\TVGUIDEROOT\TV GUIDE (fixed, well-known fallback location)
    ''' Returns Nothing if none of these are set up yet, so callers fall back to
    ''' LastRootFolder / their own hardcoded defaults.
    Public Function GetAutoDetectedRootFolder() As String
        Dim linked = GetLinkedRootFolder()
        If linked IsNot Nothing Then Return linked
        Dim published = GetPublishedRootFolder()
        If published IsNot Nothing Then Return published
        Return GetFixedRootFolder()
    End Function

End Module
