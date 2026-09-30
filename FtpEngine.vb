Imports System
Imports System.IO
Imports System.Net
Imports System.Collections.Generic
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading.Tasks

Public Class FtpEngine
    Private _host As String
    Private _port As Integer
    Private _username As String
    Private _password As String
    Private _passive As Boolean
    Private _connected As Boolean = False
    
    Public Event LogMessage(message As String)
    
    Public Property IsConnected As Boolean
        Get
            Return _connected
        End Get
    End Property
    
    Public Sub New(host As String, port As Integer, username As String, password As String, passive As Boolean)
        _host = host
        _port = port
        _username = username
        _password = password
        _passive = passive
    End Sub
    
    Public Async Function TestConnectionAsync() As Task(Of Boolean)
        Try
            RaiseEvent LogMessage("Bağlantı test ediliyor: " & _host)
            
            Dim uri As String = "ftp://" & _host & ":"
            If _port <= 0 Then _port = 21
            uri &= _port.ToString() & "/"
            
            Dim request As FtpWebRequest = DirectCast(WebRequest.Create(uri), FtpWebRequest)
            request.Method = WebRequestMethods.Ftp.ListDirectory
            request.Credentials = New NetworkCredential(_username, _password)
            request.UsePassive = _passive
            request.Timeout = 10000
            
            Dim response As FtpWebResponse = Await Task.Factory.FromAsync(
                AddressOf request.BeginGetResponse,
                Function(ar) DirectCast(request.EndGetResponse(ar), FtpWebResponse),
                Nothing)
            
            response.Close()
            _connected = True
            RaiseEvent LogMessage("Bağlantı başarılı!")
            Return True
            
        Catch ex As Exception
            _connected = False
            RaiseEvent LogMessage("Bağlantı hatası: " & ex.Message)
            Return False
        End Try
    End Function
    
    Public Async Function ListDirectoryAsync(path As String) As Task(Of List(Of RemoteFile))
        Dim result As New List(Of RemoteFile)
        
        Try
            If Not path.EndsWith("/") Then path &= "/"
            RaiseEvent LogMessage("Listeleniyor: " & path)
            
            Dim uri As String = "ftp://" & _host & ":"
            If _port <= 0 Then _port = 21
            uri &= _port.ToString() & path
            
            Dim request As FtpWebRequest = DirectCast(WebRequest.Create(uri), FtpWebRequest)
            request.Method = WebRequestMethods.Ftp.ListDirectoryDetails
            request.Credentials = New NetworkCredential(_username, _password)
            request.UsePassive = _passive
            request.Timeout = 10000
            
            Dim response As FtpWebResponse = Await Task.Factory.FromAsync(
                AddressOf request.BeginGetResponse,
                Function(ar) DirectCast(request.EndGetResponse(ar), FtpWebResponse),
                Nothing)
            
            Using stream = response.GetResponseStream()
                Using reader As New StreamReader(stream)
                    Dim content As String = Await reader.ReadToEndAsync()
                    Dim lines As String() = content.Split(New String() {vbCrLf, vbLf}, StringSplitOptions.RemoveEmptyEntries)
                    
                    For Each line In lines
                        Dim item = ParseFtpLine(line, path)
                        If item IsNot Nothing Then
                            result.Add(item)
                        End If
                    Next
                End Using
            End Using
            
            response.Close()
            RaiseEvent LogMessage(String.Format("Liste alındı: {0} dosya", result.Count))
            
        Catch ex As Exception
            RaiseEvent LogMessage("Listeleme hatası: " & ex.Message)
        End Try
        
        Return result
    End Function
    
    Public Async Function DownloadFileAsync(remotePath As String, localPath As String) As Task(Of Boolean)
        Try
            RaiseEvent LogMessage(String.Format("İndiriliyor: {0}", remotePath))
            
            Dim uri As String = "ftp://" & _host & ":"
            If _port <= 0 Then _port = 21
            uri &= _port.ToString() & remotePath
            
            Dim request As FtpWebRequest = DirectCast(WebRequest.Create(uri), FtpWebRequest)
            request.Method = WebRequestMethods.Ftp.DownloadFile
            request.Credentials = New NetworkCredential(_username, _password)
            request.UsePassive = _passive
            request.Timeout = 10000
            
            Dim response As FtpWebResponse = Await Task.Factory.FromAsync(
                AddressOf request.BeginGetResponse,
                Function(ar) DirectCast(request.EndGetResponse(ar), FtpWebResponse),
                Nothing)
            
            Using responseStream = response.GetResponseStream()
                Using fileStream As New FileStream(localPath, FileMode.Create, FileAccess.Write)
                    Await responseStream.CopyToAsync(fileStream)
                End Using
            End Using
            
            response.Close()
            RaiseEvent LogMessage("İndirme tamamlandı!")
            Return True
            
        Catch ex As Exception
            RaiseEvent LogMessage("İndirme hatası: " & ex.Message)
            Return False
        End Try
    End Function
    
    Public Async Function UploadFileAsync(localPath As String, remotePath As String) As Task(Of Boolean)
        Try
            RaiseEvent LogMessage(String.Format("Yükleniliyor: {0}", localPath))
            
            Dim uri As String = "ftp://" & _host & ":"
            If _port <= 0 Then _port = 21
            uri &= _port.ToString() & remotePath
            
            Dim request As FtpWebRequest = DirectCast(WebRequest.Create(uri), FtpWebRequest)
            request.Method = WebRequestMethods.Ftp.UploadFile
            request.Credentials = New NetworkCredential(_username, _password)
            request.UsePassive = _passive
            request.Timeout = 10000
            
            Dim fi As New FileInfo(localPath)
            request.ContentLength = fi.Length
            
            Using fileStream As New FileStream(localPath, FileMode.Open, FileAccess.Read)
                Using requestStream = Await Task.Factory.FromAsync(
                    AddressOf request.BeginGetRequestStream,
                    Function(ar) request.EndGetRequestStream(ar),
                    Nothing)
                    Await fileStream.CopyToAsync(requestStream)
                End Using
            End Using
            
            Dim response As FtpWebResponse = Await Task.Factory.FromAsync(
                AddressOf request.BeginGetResponse,
                Function(ar) DirectCast(request.EndGetResponse(ar), FtpWebResponse),
                Nothing)
            
            response.Close()
            RaiseEvent LogMessage("Yükleme tamamlandı!")
            Return True
            
        Catch ex As Exception
            RaiseEvent LogMessage("Yükleme hatası: " & ex.Message)
            Return False
        End Try
    End Function
    
    Public Async Function DeleteFileAsync(remotePath As String) As Task(Of Boolean)
        Try
            Dim uri As String = "ftp://" & _host & ":"
            If _port <= 0 Then _port = 21
            uri &= _port.ToString() & remotePath
            
            Dim request As FtpWebRequest = DirectCast(WebRequest.Create(uri), FtpWebRequest)
            request.Method = WebRequestMethods.Ftp.DeleteFile
            request.Credentials = New NetworkCredential(_username, _password)
            request.UsePassive = _passive
            
            Dim response As FtpWebResponse = Await Task.Factory.FromAsync(
                AddressOf request.BeginGetResponse,
                Function(ar) DirectCast(request.EndGetResponse(ar), FtpWebResponse),
                Nothing)
            
            response.Close()
            RaiseEvent LogMessage(String.Format("Silindi: {0}", remotePath))
            Return True
            
        Catch ex As Exception
            RaiseEvent LogMessage("Silme hatası: " & ex.Message)
            Return False
        End Try
    End Function
    
    Private Function ParseFtpLine(line As String, basePath As String) As RemoteFile
        Try
            ' Unix format: -rw-r--r-- 1 owner group 1234 Jan 01 12:00 filename
            Dim pattern As String = "^([d\-]).*\s+(\S+)\s+(\d+)\s+(\w+\s+\d+\s+[\d:]+)\s+(.+)$"
            Dim match As Match = Regex.Match(line, pattern)
            
            If Not match.Success Then Return Nothing
            
            Dim isDir As Boolean = match.Groups(1).Value = "d"
            Dim size As Long = CLng(match.Groups(3).Value)
            Dim name As String = match.Groups(5).Value.Trim()
            
            Return New RemoteFile With {
                .Name = name,
                .FullPath = basePath & name,
                .IsDirectory = isDir,
                .Size = size,
                .Modified = DateTime.Now
            }
            
        Catch
            Return Nothing
        End Try
    End Function
End Class