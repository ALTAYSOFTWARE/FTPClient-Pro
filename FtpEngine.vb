Imports System.Linq
Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Net
Imports System.Net.Security
Imports System.Security.Cryptography.X509Certificates
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Diagnostics
Imports Microsoft.VisualBasic
Imports Renci.SshNet
Imports Renci.SshNet.Sftp

Public Class FtpEngine
    Implements System.IDisposable

    Private ReadOnly _site As FtpSite
    Private _disposed As Boolean = False

    ' SFTP MOTORU
    Private _sftpClient As SftpClient

    Public Event LogMessage(message As String, level As LogLevel)
    Public Property IsConnected As Boolean = False
    Public Property CurrentDirectory As String = "/"
    Public Property SpeedLimitKbps As Integer = 0 ' 0 = Sınırsız

    Public Sub New(site As FtpSite)
        _site = site
        ServicePointManager.ServerCertificateValidationCallback =
            New RemoteCertificateValidationCallback(AddressOf ValidateCert)
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 Or SecurityProtocolType.Tls
    End Sub

    Private Function ValidateCert(sender As Object, cert As X509Certificate, chain As X509Chain, errors As SslPolicyErrors) As Boolean
        Return True
    End Function

    ' =========================================================================
    ' BAĞLANTI YÖNETİMİ (DUAL ENGINE)
    ' =========================================================================
    Public Async Function TestConnectionAsync() As Task(Of Boolean)
        Try
            Log("Bağlantı test ediliyor: " & _site.Host, LogLevel.Info)

            If _site.Protocol = FtpProtocol.SFTP Then
                ' --- SFTP BAĞLANTISI (SSH.NET) ---
                Dim port As Integer = If(_site.Port > 0, _site.Port, 22)
                _sftpClient = New SftpClient(_site.Host, port, _site.Username, _site.Password)
                Await Task.Run(Sub() _sftpClient.Connect())
                IsConnected = True
                Log("SFTP Bağlantısı başarılı: " & _site.Host, LogLevel.Success)
                Return True
            Else
                ' --- FTP / FTPS BAĞLANTISI ---
                Dim uriStr As String = "ftp://" & _site.Host & ":" & _site.Port.ToString() & "/"
                Dim req As FtpWebRequest = DirectCast(WebRequest.Create(uriStr), FtpWebRequest)
                req.Method = WebRequestMethods.Ftp.ListDirectory
                req.Credentials = New NetworkCredential(
                If(String.IsNullOrEmpty(_site.Username), "anonymous", _site.Username),
                If(String.IsNullOrEmpty(_site.Password), "anonymous@", _site.Password))
                req.UsePassive = _site.PassiveMode
                req.UseBinary = True
                req.KeepAlive = False
                req.Timeout = _site.TimeoutSeconds * 1000
                If _site.Protocol = FtpProtocol.FTPS_Implicit OrElse _site.Protocol = FtpProtocol.FTPES_Explicit Then
                    req.EnableSsl = True
                End If
                Dim resp As FtpWebResponse = Await Task.Factory.FromAsync(
                AddressOf req.BeginGetResponse,
                Function(ar) DirectCast(req.EndGetResponse(ar), FtpWebResponse),
                Nothing)
                resp.Close()
                IsConnected = True
                Log("FTP Bağlantısı başarılı: " & _site.Host, LogLevel.Success)
                Return True
            End If
        Catch ex As Exception
            Log("Bağlantı hatası: " & ex.Message, LogLevel.Error)
            IsConnected = False
            Return False
        End Try
    End Function

    ' =========================================================================
    ' KLASÖR LİSTELEME (DUAL ENGINE)
    ' =========================================================================
    Public Async Function ListDirectoryAsync(path As String) As Task(Of List(Of FtpFileItem))
        Dim result As New List(Of FtpFileItem)()
        Try
            If Not path.EndsWith("/") Then path = path & "/"
            Log("LIST " & path, LogLevel.Command)

            If _site.Protocol = FtpProtocol.SFTP Then
                ' --- SFTP LİSTELEME ---
                result = Await Task.Run(Function()
                                            Dim sftpList As New List(Of FtpFileItem)
                                            Dim files = _sftpClient.ListDirectory(path)
                                            For Each f In files
                                                If f.Name <> "." AndAlso f.Name <> ".." Then
                                                    ' SSH.NET İzinlerini Linux Formatına (drwxr-xr-x) Çevirme
                                                    Dim pStr As String = If(f.IsDirectory, "d", "-") &
                                                                         If(f.Attributes.OwnerCanRead, "r", "-") & If(f.Attributes.OwnerCanWrite, "w", "-") & If(f.Attributes.OwnerCanExecute, "x", "-") &
                                                                         If(f.Attributes.GroupCanRead, "r", "-") & If(f.Attributes.GroupCanWrite, "w", "-") & If(f.Attributes.GroupCanExecute, "x", "-") &
                                                                         If(f.Attributes.OthersCanRead, "r", "-") & If(f.Attributes.OthersCanWrite, "w", "-") & If(f.Attributes.OthersCanExecute, "x", "-")

                                                    Dim item As New FtpFileItem() With {
                                                        .Name = f.Name,
                                                        .IsDirectory = f.IsDirectory,
                                                        .IsSymlink = f.IsSymbolicLink,
                                                        .Size = f.Attributes.Size,
                                                        .LastModified = f.LastWriteTime,
                                                        .FullPath = path & f.Name,
                                                        .Permissions = pStr,
                                                        .Owner = f.Attributes.UserId.ToString()
                                                    }
                                                    sftpList.Add(item)
                                                End If
                                            Next
                                            Return sftpList
                                        End Function)
                Log("SFTP Liste alındı", LogLevel.Response)
            Else
                ' --- FTP LİSTELEME ---
                Dim req As FtpWebRequest = CreateRequest(path, WebRequestMethods.Ftp.ListDirectoryDetails)
                Dim resp As FtpWebResponse = Await Task.Factory.FromAsync(
                    AddressOf req.BeginGetResponse,
                    Function(ar) DirectCast(req.EndGetResponse(ar), FtpWebResponse),
                    Nothing)
                Dim content As String
                Using sr As New StreamReader(resp.GetResponseStream(), System.Text.Encoding.Default)
                    content = Await sr.ReadToEndAsync()
                End Using
                resp.Close()
                Log("FTP Liste alındı", LogLevel.Response)
                result = ParseList(content, path)
            End If
        Catch ex As Exception
            Log("Listeleme hatası: " & ex.Message, LogLevel.Error)
        End Try
        Return result
    End Function

    ' =========================================================================
    ' DOSYA İNDİRME (DUAL ENGINE - ORTAK HIZ SINIRLAYICI)
    ' =========================================================================
    Public Async Function DownloadFileAsync(transfer As TransferItem, cancelToken As CancellationToken) As Task(Of Boolean)
        Dim speedSw As New Stopwatch()
        Dim speedBytes As Long = 0
        Dim resp As FtpWebResponse = Nothing
        Dim netStream As Stream = Nothing

        Try
            Log("RETR " & transfer.RemotePath, LogLevel.Command)
            transfer.Status = TransferStatus.Connecting
            transfer.StartTime = DateTime.Now

            Dim localDir As String = Path.GetDirectoryName(transfer.LocalPath)
            If Not Directory.Exists(localDir) Then Directory.CreateDirectory(localDir)

            ' Stream'i protokole göre açıyoruz
            If _site.Protocol = FtpProtocol.SFTP Then
                netStream = _sftpClient.OpenRead(transfer.RemotePath)
            Else
                Dim req As FtpWebRequest = CreateRequest(transfer.RemotePath, WebRequestMethods.Ftp.DownloadFile)
                resp = Await Task.Factory.FromAsync(AddressOf req.BeginGetResponse, Function(ar) DirectCast(req.EndGetResponse(ar), FtpWebResponse), Nothing)
                netStream = resp.GetResponseStream()
            End If

            transfer.Status = TransferStatus.Transferring
            speedSw.Start()

            Using netStream
                Using fs As New FileStream(transfer.LocalPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536)
                    Dim buffer(65535) As Byte
                    Dim totalRead As Long = 0
                    Dim bytesRead As Integer = Await netStream.ReadAsync(buffer, 0, buffer.Length, cancelToken)

                    Do While bytesRead > 0
                        cancelToken.ThrowIfCancellationRequested()
                        Await fs.WriteAsync(buffer, 0, bytesRead, cancelToken)
                        totalRead += bytesRead
                        speedBytes += bytesRead

                        If SpeedLimitKbps > 0 Then
                            Dim elapsedSecs = (DateTime.Now - transfer.StartTime).TotalSeconds
                            If elapsedSecs > 0 Then
                                Dim targetSecs = (totalRead / 1024.0) / SpeedLimitKbps
                                If targetSecs > elapsedSecs Then
                                    Dim delayMs = CInt((targetSecs - elapsedSecs) * 1000)
                                    If delayMs > 10 Then Await Task.Delay(delayMs, cancelToken)
                                End If
                            End If
                        End If

                        If speedSw.ElapsedMilliseconds >= 500 Then
                            transfer.TransferredBytes = totalRead
                            transfer.Speed = speedBytes / (speedSw.ElapsedMilliseconds / 1000.0)
                            If transfer.FileSize > 0 Then transfer.Progress = (CDbl(totalRead) / CDbl(transfer.FileSize)) * 100.0
                            speedBytes = 0
                            speedSw.Restart()
                        End If

                        bytesRead = Await netStream.ReadAsync(buffer, 0, buffer.Length, cancelToken)
                    Loop
                End Using
            End Using

            If resp IsNot Nothing Then resp.Close()

            transfer.TransferredBytes = transfer.FileSize
            transfer.Progress = 100
            transfer.Speed = 0
            transfer.Status = TransferStatus.Completed
            transfer.EndTime = DateTime.Now
            Log("İndirme tamamlandı: " & transfer.FileName, LogLevel.Success)
            Return True

        Catch ex As OperationCanceledException
            transfer.Status = TransferStatus.Cancelled
            Log("İptal: " & transfer.FileName, LogLevel.Warning)
            If File.Exists(transfer.LocalPath) Then
                Try : File.Delete(transfer.LocalPath) : Catch : End Try
            End If
            Return False
        Catch ex As Exception
            transfer.Status = TransferStatus.Failed
            transfer.ErrorMessage = ex.Message
            Log("İndirme hatası: " & ex.Message, LogLevel.Error)
            Return False
        End Try
    End Function

    ' =========================================================================
    ' DOSYA YÜKLEME (DUAL ENGINE - ORTAK HIZ SINIRLAYICI)
    ' =========================================================================
    Public Async Function UploadFileAsync(transfer As TransferItem, cancelToken As CancellationToken) As Task(Of Boolean)
        Dim speedSw As New Stopwatch()
        Dim speedBytes As Long = 0
        Dim req As FtpWebRequest = Nothing
        Dim reqStream As Stream = Nothing

        Try
            Log("STOR " & transfer.RemotePath, LogLevel.Command)
            transfer.Status = TransferStatus.Connecting
            transfer.StartTime = DateTime.Now

            If Not File.Exists(transfer.LocalPath) Then Throw New FileNotFoundException("Dosya bulunamadı: " & transfer.LocalPath)
            Dim fi As New FileInfo(transfer.LocalPath)
            transfer.FileSize = fi.Length

            If _site.Protocol = FtpProtocol.SFTP Then
                reqStream = _sftpClient.OpenWrite(transfer.RemotePath)
            Else
                req = CreateRequest(transfer.RemotePath, WebRequestMethods.Ftp.UploadFile)
                req.ContentLength = fi.Length
                reqStream = Await Task.Factory.FromAsync(AddressOf req.BeginGetRequestStream, Function(ar) req.EndGetRequestStream(ar), Nothing)
            End If

            transfer.Status = TransferStatus.Transferring
            speedSw.Start()

            Using reqStream
                Using fs As New FileStream(transfer.LocalPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536)
                    Dim buffer(65535) As Byte
                    Dim totalSent As Long = 0
                    Dim bytesRead As Integer = Await fs.ReadAsync(buffer, 0, buffer.Length, cancelToken)

                    Do While bytesRead > 0
                        cancelToken.ThrowIfCancellationRequested()
                        Await reqStream.WriteAsync(buffer, 0, bytesRead, cancelToken)
                        totalSent += bytesRead
                        speedBytes += bytesRead

                        If SpeedLimitKbps > 0 Then
                            Dim elapsedSecs = (DateTime.Now - transfer.StartTime).TotalSeconds
                            If elapsedSecs > 0 Then
                                Dim targetSecs = (totalSent / 1024.0) / SpeedLimitKbps
                                If targetSecs > elapsedSecs Then
                                    Dim delayMs = CInt((targetSecs - elapsedSecs) * 1000)
                                    If delayMs > 10 Then Await Task.Delay(delayMs, cancelToken)
                                End If
                            End If
                        End If

                        If speedSw.ElapsedMilliseconds >= 500 Then
                            transfer.TransferredBytes = totalSent
                            transfer.Speed = speedBytes / (speedSw.ElapsedMilliseconds / 1000.0)
                            If fi.Length > 0 Then transfer.Progress = (CDbl(totalSent) / CDbl(fi.Length)) * 100.0
                            speedBytes = 0
                            speedSw.Restart()
                        End If

                        bytesRead = Await fs.ReadAsync(buffer, 0, buffer.Length, cancelToken)
                    Loop
                End Using
            End Using

            If req IsNot Nothing Then
                Dim resp As FtpWebResponse = Await Task.Factory.FromAsync(AddressOf req.BeginGetResponse, Function(ar) DirectCast(req.EndGetResponse(ar), FtpWebResponse), Nothing)
                resp.Close()
            End If

            transfer.TransferredBytes = transfer.FileSize
            transfer.Progress = 100
            transfer.Speed = 0
            transfer.Status = TransferStatus.Completed
            transfer.EndTime = DateTime.Now
            Log("Yükleme tamamlandı: " & transfer.FileName, LogLevel.Success)
            Return True

        Catch ex As OperationCanceledException
            transfer.Status = TransferStatus.Cancelled
            Log("İptal: " & transfer.FileName, LogLevel.Warning)
            Return False
        Catch ex As Exception
            transfer.Status = TransferStatus.Failed
            transfer.ErrorMessage = ex.Message
            Log("Yükleme hatası: " & ex.Message, LogLevel.Error)
            Return False
        End Try
    End Function

    ' =========================================================================
    ' DOSYA & KLASÖR İŞLEMLERİ (DUAL ENGINE)
    ' =========================================================================
    Public Async Function DeleteFileAsync(remotePath As String) As Task(Of Boolean)
        Try
            Log("DELE " & remotePath, LogLevel.Command)
            If _site.Protocol = FtpProtocol.SFTP Then
                Await Task.Run(Sub() _sftpClient.DeleteFile(remotePath))
            Else
                Dim req As FtpWebRequest = CreateRequest(remotePath, WebRequestMethods.Ftp.DeleteFile)
                Dim resp As FtpWebResponse = Await Task.Factory.FromAsync(AddressOf req.BeginGetResponse, Function(ar) DirectCast(req.EndGetResponse(ar), FtpWebResponse), Nothing)
                resp.Close()
            End If
            Return True
        Catch ex As Exception
            Log("Silme hatası: " & ex.Message, LogLevel.Error)
            Return False
        End Try
    End Function

    Public Async Function DeleteDirectoryAsync(remotePath As String) As Task(Of Boolean)
        Try
            Dim items As List(Of FtpFileItem) = Await ListDirectoryAsync(remotePath)
            For Each item As FtpFileItem In items
                If item.IsDirectory Then
                    Await DeleteDirectoryAsync(remotePath.TrimEnd("/"c) & "/" & item.Name)
                Else
                    Await DeleteFileAsync(remotePath.TrimEnd("/"c) & "/" & item.Name)
                End If
            Next
            Log("RMD " & remotePath, LogLevel.Command)
            If _site.Protocol = FtpProtocol.SFTP Then
                Await Task.Run(Sub() _sftpClient.DeleteDirectory(remotePath))
            Else
                Dim req As FtpWebRequest = CreateRequest(remotePath, WebRequestMethods.Ftp.RemoveDirectory)
                Dim resp As FtpWebResponse = Await Task.Factory.FromAsync(AddressOf req.BeginGetResponse, Function(ar) DirectCast(req.EndGetResponse(ar), FtpWebResponse), Nothing)
                resp.Close()
            End If
            Return True
        Catch ex As Exception
            Log("Klasör silme hatası: " & ex.Message, LogLevel.Error)
            Return False
        End Try
    End Function

    Public Async Function CreateDirectoryAsync(remotePath As String) As Task(Of Boolean)
        Try
            Log("MKD " & remotePath, LogLevel.Command)
            If _site.Protocol = FtpProtocol.SFTP Then
                Await Task.Run(Sub() _sftpClient.CreateDirectory(remotePath))
            Else
                Dim req As FtpWebRequest = CreateRequest(remotePath, WebRequestMethods.Ftp.MakeDirectory)
                Dim resp As FtpWebResponse = Await Task.Factory.FromAsync(AddressOf req.BeginGetResponse, Function(ar) DirectCast(req.EndGetResponse(ar), FtpWebResponse), Nothing)
                resp.Close()
            End If
            Return True
        Catch ex As WebException
            Dim r As FtpWebResponse = TryCast(ex.Response, FtpWebResponse)
            If r IsNot Nothing AndAlso r.StatusCode = FtpStatusCode.ActionNotTakenFileUnavailable Then Return True
            Log("Klasör oluşturma hatası: " & ex.Message, LogLevel.Error)
            Return False
        Catch ex As Exception
            Log("Klasör oluşturma hatası: " & ex.Message, LogLevel.Error)
            Return False
        End Try
    End Function

    Public Async Function RenameAsync(oldPath As String, newPath As String) As Task(Of Boolean)
        Try
            Log("RNFR " & oldPath, LogLevel.Command)
            If _site.Protocol = FtpProtocol.SFTP Then
                Await Task.Run(Sub() _sftpClient.RenameFile(oldPath, newPath))
            Else
                Dim req As FtpWebRequest = CreateRequest(oldPath, WebRequestMethods.Ftp.Rename)
                req.RenameTo = newPath
                Dim resp As FtpWebResponse = Await Task.Factory.FromAsync(AddressOf req.BeginGetResponse, Function(ar) DirectCast(req.EndGetResponse(ar), FtpWebResponse), Nothing)
                resp.Close()
            End If
            Return True
        Catch ex As Exception
            Log("Yeniden adlandırma hatası: " & ex.Message, LogLevel.Error)
            Return False
        End Try
    End Function

    Public Async Function GetFileSizeAsync(remotePath As String) As Task(Of Long)
        Try
            If _site.Protocol = FtpProtocol.SFTP Then
                Return Await Task.Run(Function() _sftpClient.GetAttributes(remotePath).Size)
            Else
                Dim req As FtpWebRequest = CreateRequest(remotePath, WebRequestMethods.Ftp.GetFileSize)
                Dim resp As FtpWebResponse = Await Task.Factory.FromAsync(AddressOf req.BeginGetResponse, Function(ar) DirectCast(req.EndGetResponse(ar), FtpWebResponse), Nothing)
                Dim size As Long = resp.ContentLength
                resp.Close()
                Return size
            End If
        Catch
            Return 0
        End Try
    End Function

    Public Async Function SetPermissionsAsync(remotePath As String, permissions As String) As Task(Of Boolean)
        Try
            If _site.Protocol = FtpProtocol.SFTP Then
                Log("SFTP CHMOD: " & permissions, LogLevel.Command)
                ' SFTP, izinleri 8'lik tabanda (Octal) integer olarak bekler. Örn: "755"
                Dim octalPermissions As Short = Convert.ToInt16(permissions, 8)
                Await Task.Run(Sub() _sftpClient.ChangePermissions(remotePath, octalPermissions))
                Return True
            Else
                ' Eski TCP Tabanlı Özel FTP CHMOD Komutu
                Dim command As String = "SITE CHMOD " & permissions & " " & remotePath
                Log("Özel TCP: " & command, LogLevel.Command)
                Using client As New Net.Sockets.TcpClient()
                    Await client.ConnectAsync(_site.Host, _site.Port)
                    Dim stream As Stream = client.GetStream()
                    If _site.Protocol = FtpProtocol.FTPS_Implicit Then
                        Dim sslStream As New SslStream(stream, False, AddressOf ValidateCert)
                        Await sslStream.AuthenticateAsClientAsync(_site.Host)
                        stream = sslStream
                    End If
                    Await ReadRawResponseAsync(stream)
                    If _site.Protocol = FtpProtocol.FTPES_Explicit Then
                        Await SendRawLineAsync(stream, "AUTH TLS")
                        Dim authReply As String = Await ReadRawResponseAsync(stream)
                        If Not authReply.StartsWith("234") Then Throw New Exception("Sunucu TLS reddetti.")
                        Dim sslStream As New SslStream(stream, False, AddressOf ValidateCert)
                        Await sslStream.AuthenticateAsClientAsync(_site.Host)
                        stream = sslStream
                    End If
                    Dim user As String = If(String.IsNullOrEmpty(_site.Username), "anonymous", _site.Username)
                    Dim pass As String = If(String.IsNullOrEmpty(_site.Password), "anonymous@", _site.Password)
                    Await SendRawLineAsync(stream, "USER " & user)
                    Dim userReply As String = Await ReadRawResponseAsync(stream)
                    If userReply.StartsWith("331") Then
                        Await SendRawLineAsync(stream, "PASS " & pass)
                        Dim passReply As String = Await ReadRawResponseAsync(stream)
                        If Not passReply.StartsWith("230") Then Throw New Exception("Giriş başarısız.")
                    End If
                    Await SendRawLineAsync(stream, command)
                    Dim chmodReply As String = Await ReadRawResponseAsync(stream)
                    Await SendRawLineAsync(stream, "QUIT")
                    If chmodReply.StartsWith("200") OrElse chmodReply.StartsWith("250") Then Return True Else Throw New Exception("CHMOD reddedildi: " & chmodReply.Trim())
                End Using
            End If
        Catch ex As Exception
            Log("CHMOD hatası: " & ex.Message, LogLevel.Error)
            Throw
        End Try
    End Function

    Public Async Function KeepAliveAsync() As Task
        Try
            If _site.Protocol = FtpProtocol.SFTP Then
                If _sftpClient IsNot Nothing AndAlso _sftpClient.IsConnected Then _sftpClient.SendKeepAlive()
            Else
                Dim req As FtpWebRequest = CreateRequest("/", WebRequestMethods.Ftp.PrintWorkingDirectory)
                Dim resp As FtpWebResponse = Await Task.Factory.FromAsync(AddressOf req.BeginGetResponse, Function(ar) DirectCast(req.EndGetResponse(ar), FtpWebResponse), Nothing)
                resp.Close()
            End If
        Catch
        End Try
    End Function

    ' =========================================================================
    ' YARDIMCI METOTLAR
    ' =========================================================================
    Private Function CreateRequest(path As String, method As String) As FtpWebRequest
        If Not path.StartsWith("/") Then path = "/" & path
        Dim uriStr As String = "ftp://" & _site.Host & ":" & _site.Port.ToString() & path
        Dim req As FtpWebRequest = DirectCast(WebRequest.Create(New Uri(uriStr)), FtpWebRequest)
        req.CachePolicy = New Net.Cache.RequestCachePolicy(Net.Cache.RequestCacheLevel.BypassCache)
        req.Method = method
        If _site.Username = "anonymous" OrElse String.IsNullOrEmpty(_site.Username) Then
            req.Credentials = New NetworkCredential("anonymous", "anonymous@")
        Else
            req.Credentials = New NetworkCredential(_site.Username, _site.Password)
        End If
        req.UsePassive = _site.PassiveMode
        req.UseBinary = True
        req.KeepAlive = _site.KeepAlive
        req.Timeout = _site.TimeoutSeconds * 1000
        req.ReadWriteTimeout = _site.TimeoutSeconds * 2000
        If _site.Protocol = FtpProtocol.FTPS_Implicit OrElse _site.Protocol = FtpProtocol.FTPES_Explicit Then req.EnableSsl = True
        Return req
    End Function

    Private Async Function SendRawLineAsync(stream As Stream, line As String) As Task
        Dim buffer As Byte() = Encoding.UTF8.GetBytes(line & vbCrLf)
        Await stream.WriteAsync(buffer, 0, buffer.Length)
    End Function

    Private Async Function ReadRawResponseAsync(stream As Stream) As Task(Of String)
        Dim buffer(4096) As Byte
        Dim response As String = ""
        While True
            Dim bytesRead As Integer = Await stream.ReadAsync(buffer, 0, buffer.Length)
            If bytesRead = 0 Then Exit While
            response &= Encoding.UTF8.GetString(buffer, 0, bytesRead)
            Dim lines As String() = response.Split(New String() {vbCrLf, vbLf}, StringSplitOptions.RemoveEmptyEntries)
            If lines.Length > 0 Then
                Dim lastLine As String = lines(lines.Length - 1)
                If lastLine.Length >= 4 AndAlso Char.IsDigit(lastLine(0)) AndAlso Char.IsDigit(lastLine(1)) AndAlso Char.IsDigit(lastLine(2)) AndAlso lastLine(3) = " "c Then Exit While
            End If
        End While
        Return response
    End Function

    Private Function ParseList(content As String, basePath As String) As List(Of FtpFileItem)
        Dim result As New List(Of FtpFileItem)()
        Dim lines As String() = content.Split(New Char() {ControlChars.Lf, ControlChars.Cr}, StringSplitOptions.RemoveEmptyEntries)
        For Each line As String In lines
            Dim trimmed As String = line.Trim()
            If String.IsNullOrEmpty(trimmed) Then Continue For
            Dim item As FtpFileItem = ParseUnixLine(trimmed, basePath)
            If item Is Nothing Then item = ParseWindowsLine(trimmed, basePath)
            If item IsNot Nothing AndAlso item.Name <> "." AndAlso item.Name <> ".." Then result.Add(item)
        Next
        Return result
    End Function

    Private Function ParseUnixLine(line As String, basePath As String) As FtpFileItem
        Try
            Dim pattern As String = "^([d\-lbcps])([rwxsStT\-]{9})\s+(\d+)\s+(\S+)\s+(\S+)\s+(\d+)\s+(\w+\s+\d+\s+[\d:]+)\s+(.+)$"
            Dim m As Match = Regex.Match(line, pattern)
            If Not m.Success Then Return Nothing
            Dim item As New FtpFileItem()
            item.IsDirectory = m.Groups(1).Value = "d"
            item.IsSymlink = m.Groups(1).Value = "l"
            item.Permissions = m.Groups(1).Value & m.Groups(2).Value
            item.Owner = m.Groups(4).Value
            Dim sz As Long = 0
            Long.TryParse(m.Groups(6).Value, sz)
            item.Size = sz
            item.LastModified = ParseFtpDate(m.Groups(7).Value.Trim())
            Dim namePart As String = m.Groups(8).Value.Trim()
            If item.IsSymlink AndAlso namePart.Contains(" -> ") Then namePart = namePart.Split(New String() {" -> "}, StringSplitOptions.None)(0)
            item.Name = namePart
            item.FullPath = basePath.TrimEnd("/"c) & "/" & namePart
            Return item
        Catch
            Return Nothing
        End Try
    End Function

    Private Function ParseWindowsLine(line As String, basePath As String) As FtpFileItem
        Try
            Dim pattern As String = "^(\d{2}-\d{2}-\d{2,4})\s+(\d{2}:\d{2}[AP]M)\s+(<DIR>|\d+)\s+(.+)$"
            Dim m As Match = Regex.Match(line, pattern)
            If Not m.Success Then Return Nothing
            Dim item As New FtpFileItem()
            Dim sizeOrDir As String = m.Groups(3).Value
            If sizeOrDir = "<DIR>" Then
                item.IsDirectory = True
            Else
                Dim sz As Long = 0
                Long.TryParse(sizeOrDir, sz)
                item.Size = sz
            End If
            item.Name = m.Groups(4).Value.Trim()
            item.FullPath = basePath.TrimEnd("/"c) & "/" & item.Name
            Return item
        Catch
            Return Nothing
        End Try
    End Function

    Private Function ParseFtpDate(dateStr As String) As DateTime
        Try
            Dim months As New Dictionary(Of String, Integer)() From {{"jan", 1}, {"feb", 2}, {"mar", 3}, {"apr", 4}, {"may", 5}, {"jun", 6}, {"jul", 7}, {"aug", 8}, {"sep", 9}, {"oct", 10}, {"nov", 11}, {"dec", 12}}
            Dim parts As String() = dateStr.Split(New Char() {" "c}, StringSplitOptions.RemoveEmptyEntries)
            If parts.Length < 3 Then Return DateTime.MinValue
            Dim month As Integer = 1
            months.TryGetValue(parts(0).ToLower(), month)
            Dim day As Integer = CInt(parts(1))
            Dim year As Integer, hour As Integer = 0, minute As Integer = 0
            If parts(2).Contains(":") Then
                year = DateTime.Now.Year
                Dim tp As String() = parts(2).Split(":"c)
                hour = CInt(tp(0)) : minute = CInt(tp(1))
            Else
                year = CInt(parts(2))
            End If
            Return New DateTime(year, month, day, hour, minute, 0)
        Catch
            Return DateTime.MinValue
        End Try
    End Function

    Private Sub Log(message As String, level As LogLevel)
        RaiseEvent LogMessage(message, level)
    End Sub

    Public Sub Dispose() Implements System.IDisposable.Dispose
        If Not _disposed Then
            IsConnected = False
            If _sftpClient IsNot Nothing Then
                Try
                    If _sftpClient.IsConnected Then _sftpClient.Disconnect()
                Catch ex As Exception
                End Try

                Try
                    _sftpClient.Dispose()
                Catch ex As Exception
                End Try
            End If
            _disposed = True
        End If
    End Sub
End Class