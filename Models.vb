Imports System
Imports System.Collections.Generic
Imports System.Collections.ObjectModel
Imports System.ComponentModel
Imports System.Diagnostics
Imports System.IO
Imports System.Linq
Imports System.Runtime.Serialization
Imports System.Runtime.Serialization.Json
Imports System.Text
Imports Microsoft.VisualBasic

Public Enum FtpProtocol
    FTP = 0
    FTPS_Implicit = 1
    FTPES_Explicit = 2
    SFTP = 3
End Enum

Public Enum TransferDirection
    Upload
    Download
End Enum

Public Enum TransferStatus
    Queued
    Connecting
    Transferring
    Completed
    Failed
    Paused
    Cancelled
End Enum

Public Enum LogLevel
    Info
    Success
    Warning
    [Error]
    Command
    Response
End Enum

<DataContract>
Public Class FtpSite
    <DataMember> Public Property Name As String = "Yeni Site"
    <DataMember> Public Property Host As String = ""
    <DataMember> Public Property Port As Integer = 21
    <DataMember> Public Property Username As String = ""
    <DataMember> Public Property Password As String = ""
    <DataMember> Public Property RemotePath As String = "/"
    <DataMember> Public Property LocalPath As String = ""
    <DataMember> Public Property PassiveMode As Boolean = True
    <DataMember> Public Property KeepAlive As Boolean = True
    <DataMember> Public Property Protocol As Integer = 0
    <DataMember> Public Property Comments As String = ""
    <DataMember> Public Property LastConnected As DateTime? = Nothing
    <DataMember> Public Property TimeoutSeconds As Integer = 30

    Public ReadOnly Property LastConnectedDisplay As String
        Get
            If Not LastConnected.HasValue Then Return "Hiç bağlanılmadı"
            Return LastConnected.Value.ToString("dd.MM.yyyy HH:mm")
        End Get
    End Property

    Public Sub New()
    End Sub

    Public Function CloneSite() As FtpSite
        Return New FtpSite With {
            .Name = Me.Name & " (Kopya)",
            .Host = Me.Host,
            .Port = Me.Port,
            .Username = Me.Username,
            .Password = Me.Password,
            .RemotePath = Me.RemotePath,
            .LocalPath = Me.LocalPath,
            .PassiveMode = Me.PassiveMode,
            .KeepAlive = Me.KeepAlive,
            .Protocol = Me.Protocol,
            .Comments = Me.Comments,
            .TimeoutSeconds = Me.TimeoutSeconds
        }
    End Function
End Class

Public Class FtpFileItem
    Public Property Name As String = ""
    Public Property FullPath As String = ""
    Public Property Size As Long = 0
    Public Property LastModified As DateTime = DateTime.MinValue
    Public Property IsDirectory As Boolean = False
    Public Property Permissions As String = ""
    Public Property Owner As String = ""
    Public Property IsSymlink As Boolean = False
    Public Property ComparisonStatus As String = "Normal"
    
    Public ReadOnly Property SizeDisplay As String
        Get
            If IsDirectory Then Return ""
            Return FormatBytes(Size)
        End Get
    End Property

    Public ReadOnly Property TypeDisplay As String
        Get
            If IsDirectory Then Return "Klasor"
            Dim ext As String = Path.GetExtension(Name).ToLowerInvariant()
            Select Case ext
                Case ".zip", ".rar", ".7z", ".tar", ".gz" : Return "Arsiv"
                Case ".jpg", ".jpeg", ".png", ".gif", ".bmp" : Return "Resim"
                Case ".mp3", ".wav", ".flac" : Return "Ses"
                Case ".mp4", ".avi", ".mkv" : Return "Video"
                Case ".pdf" : Return "PDF"
                Case ".doc", ".docx" : Return "Word"
                Case ".xls", ".xlsx" : Return "Excel"
                Case ".html", ".htm" : Return "HTML"
                Case ".txt", ".log" : Return "Metin"
                Case ".exe", ".msi" : Return "Uygulama"
                Case "" : Return "Dosya"
                Case Else : Return ext.TrimStart("."c).ToUpperInvariant()
            End Select
        End Get
    End Property

    Public ReadOnly Property LastModifiedDisplay As String
        Get
            If LastModified = DateTime.MinValue Then Return ""
            Return LastModified.ToString("dd.MM.yyyy HH:mm")
        End Get
    End Property

    Public ReadOnly Property IconText As String
        Get
            If IsDirectory Then Return Char.ConvertFromUtf32(128193)
            Dim ext As String = Path.GetExtension(Name).ToLowerInvariant()
            Select Case ext
                Case ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2" : Return Char.ConvertFromUtf32(128220)
                Case ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".svg" : Return Char.ConvertFromUtf32(128444)
                Case ".mp3", ".wav", ".flac", ".aac", ".ogg" : Return Char.ConvertFromUtf32(127925)
                Case ".mp4", ".avi", ".mkv", ".mov", ".wmv" : Return Char.ConvertFromUtf32(127916)
                Case ".pdf" : Return Char.ConvertFromUtf32(128213)
                Case ".doc", ".docx" : Return Char.ConvertFromUtf32(128209)
                Case ".xls", ".xlsx" : Return Char.ConvertFromUtf32(128202)
                Case ".html", ".htm", ".xml", ".json" : Return Char.ConvertFromUtf32(127760)
                Case ".php", ".py", ".js", ".vb", ".cs", ".cpp" : Return Char.ConvertFromUtf32(128187)
                Case ".txt", ".log", ".ini", ".cfg" : Return Char.ConvertFromUtf32(128196)
                Case ".exe", ".msi" : Return ChrW(9881)
                Case Else : Return Char.ConvertFromUtf32(128196)
            End Select
        End Get
    End Property

    Private Shared Function FormatBytes(bytes As Long) As String
        If bytes < 1024L Then Return bytes.ToString() & " B"
        If bytes < 1048576L Then Return String.Format("{0:F1} KB", bytes / 1024.0)
        If bytes < 1073741824L Then Return String.Format("{0:F1} MB", bytes / 1048576.0)
        Return String.Format("{0:F2} GB", bytes / 1073741824.0)
    End Function
End Class

Public Class LocalFileItem
    Public Property Name As String = ""
    Public Property FullPath As String = ""
    Public Property Size As Long = 0
    Public Property LastModified As DateTime = DateTime.MinValue
    Public Property IsDirectory As Boolean = False
    Public Property Extension As String = ""
    Public Property ComparisonStatus As String = "Normal"
    
    Public ReadOnly Property SizeDisplay As String
        Get
            If IsDirectory Then Return ""
            Return FormatBytes(Size)
        End Get
    End Property

    Public ReadOnly Property LastModifiedDisplay As String
        Get
            Return LastModified.ToString("dd.MM.yyyy HH:mm")
        End Get
    End Property

    Public ReadOnly Property TypeDisplay As String
        Get
            If IsDirectory Then Return "Klasor"
            If String.IsNullOrEmpty(Extension) Then Return "Dosya"
            Return Extension.TrimStart("."c).ToUpperInvariant() & " Dosyasi"
        End Get
    End Property

    Public ReadOnly Property IconText As String
        Get
            If IsDirectory Then Return Char.ConvertFromUtf32(128193)
            Select Case Extension.ToLowerInvariant()
                Case ".zip", ".rar", ".7z", ".tar", ".gz" : Return Char.ConvertFromUtf32(128220)
                Case ".jpg", ".jpeg", ".png", ".gif", ".bmp" : Return Char.ConvertFromUtf32(128444)
                Case ".mp3", ".wav", ".flac", ".aac" : Return Char.ConvertFromUtf32(127925)
                Case ".mp4", ".avi", ".mkv", ".mov" : Return Char.ConvertFromUtf32(127916)
                Case ".pdf" : Return Char.ConvertFromUtf32(128213)
                Case ".doc", ".docx" : Return Char.ConvertFromUtf32(128209)
                Case ".xls", ".xlsx" : Return Char.ConvertFromUtf32(128202)
                Case ".html", ".htm", ".xml", ".json" : Return Char.ConvertFromUtf32(127760)
                Case ".php", ".py", ".js", ".vb", ".cs" : Return Char.ConvertFromUtf32(128187)
                Case ".txt", ".log" : Return Char.ConvertFromUtf32(128196)
                Case ".exe", ".msi" : Return ChrW(9881)
                Case Else : Return Char.ConvertFromUtf32(128196)
            End Select
        End Get
    End Property

    Private Shared Function FormatBytes(bytes As Long) As String
        If bytes < 1024L Then Return bytes.ToString() & " B"
        If bytes < 1048576L Then Return String.Format("{0:F1} KB", bytes / 1024.0)
        If bytes < 1073741824L Then Return String.Format("{0:F1} MB", bytes / 1048576.0)
        Return String.Format("{0:F2} GB", bytes / 1073741824.0)
    End Function
End Class

Public Class TransferItem
    Implements INotifyPropertyChanged

    Private _status As TransferStatus = TransferStatus.Queued
    Private _progress As Double = 0
    Private _transferred As Long = 0
    Private _speed As Double = 0

    Public Property Id As String = Guid.NewGuid().ToString()
    Public Property LocalPath As String = ""
    Public Property RemotePath As String = ""
    Public Property FileName As String = ""
    Public Property FileSize As Long = 0
    Public Property Direction As TransferDirection = TransferDirection.Upload
    Public Property StartTime As DateTime = DateTime.MinValue
    Public Property EndTime As DateTime = DateTime.MinValue
    Public Property ErrorMessage As String = ""
    Public Property Site As FtpSite

    Public Property Status As TransferStatus
        Get
            Return _status
        End Get
        Set(value As TransferStatus)
            _status = value
            OnPC("Status")
            OnPC("StatusDisplay")
            OnPC("StatusColor")
            OnPC("IsActive")
            OnPC("CanCancel")
        End Set
    End Property

    Public Property Progress As Double
        Get
            Return _progress
        End Get
        Set(value As Double)
            _progress = value
            OnPC("Progress")
            OnPC("ProgressDisplay")
        End Set
    End Property

    Public Property TransferredBytes As Long
        Get
            Return _transferred
        End Get
        Set(value As Long)
            _transferred = value
            OnPC("TransferredBytes")
            OnPC("TransferredDisplay")
        End Set
    End Property

    Public Property Speed As Double
        Get
            Return _speed
        End Get
        Set(value As Double)
            _speed = value
            OnPC("Speed")
            OnPC("SpeedDisplay")
            OnPC("EtaDisplay")
        End Set
    End Property

    Public ReadOnly Property StatusDisplay As String
        Get
            Select Case Status
                Case TransferStatus.Queued : Return "Kuyrukta"
                Case TransferStatus.Connecting : Return "Baglanıyor"
                Case TransferStatus.Transferring : Return String.Format("{0:F1}%", Progress)
                Case TransferStatus.Completed : Return "Tamamlandı"
                Case TransferStatus.Failed : Return "Hata"
                Case TransferStatus.Paused : Return "Duraklatıldı"
                Case TransferStatus.Cancelled : Return "İptal"
                Case Else : Return ""
            End Select
        End Get
    End Property

    Public ReadOnly Property StatusColor As String
        Get
            Select Case Status
                Case TransferStatus.Completed : Return "#00C896"
                Case TransferStatus.Failed : Return "#FF4757"
                Case TransferStatus.Paused : Return "#FFB347"
                Case TransferStatus.Cancelled : Return "#8A9BB5"
                Case TransferStatus.Transferring, TransferStatus.Connecting : Return "#1D8CF8"
                Case Else : Return "#8A9BB5"
            End Select
        End Get
    End Property

    Public ReadOnly Property SizeDisplay As String
        Get
            Return FormatBytes(FileSize)
        End Get
    End Property

    Public ReadOnly Property TransferredDisplay As String
        Get
            Return FormatBytes(TransferredBytes)
        End Get
    End Property

    Public ReadOnly Property SpeedDisplay As String
        Get
            If Speed <= 0 Then Return ""
            Return FormatBytes(CLng(Speed)) & "/s"
        End Get
    End Property

    Public ReadOnly Property ProgressDisplay As String
        Get
            Return String.Format("{0:F1}%", Progress)
        End Get
    End Property

    Public ReadOnly Property EtaDisplay As String
        Get
            If Speed <= 0 OrElse Status <> TransferStatus.Transferring Then Return ""
            Dim remaining As Long = FileSize - TransferredBytes
            If remaining <= 0 Then Return ""
            Dim secs As Long = CLng(remaining / Speed)
            If secs < 60 Then Return secs.ToString() & "s"
            Return (secs \ 60).ToString() & "d " & (secs Mod 60).ToString() & "s"
        End Get
    End Property

    Public ReadOnly Property IsActive As Boolean
        Get
            Return Status = TransferStatus.Transferring OrElse Status = TransferStatus.Connecting
        End Get
    End Property

    Public ReadOnly Property CanCancel As Boolean
        Get
            Return Status = TransferStatus.Queued OrElse
                   Status = TransferStatus.Connecting OrElse
                   Status = TransferStatus.Transferring OrElse
                   Status = TransferStatus.Paused
        End Get
    End Property

    Private Shared Function FormatBytes(bytes As Long) As String
        If bytes < 1024L Then Return bytes.ToString() & " B"
        If bytes < 1048576L Then Return String.Format("{0:F1} KB", bytes / 1024.0)
        If bytes < 1073741824L Then Return String.Format("{0:F1} MB", bytes / 1048576.0)
        Return String.Format("{0:F2} GB", bytes / 1073741824.0)
    End Function

    Public Event PropertyChanged As PropertyChangedEventHandler Implements INotifyPropertyChanged.PropertyChanged
    Public Sub OnPC(name As String)
        RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(name))
    End Sub
End Class

Public Class LogEntry
    Public Property Time As DateTime = DateTime.Now
    Public Property Message As String = ""
    Public Property Level As LogLevel = LogLevel.Info

    Public ReadOnly Property TimeDisplay As String
        Get
            Return Time.ToString("HH:mm:ss")
        End Get
    End Property

    Public ReadOnly Property Prefix As String
        Get
            Select Case Level
                Case LogLevel.Command : Return ">>>"
                Case LogLevel.Response : Return "<<<"
                Case LogLevel.Error : Return "[HATA]"
                Case LogLevel.Warning : Return "[UYARI]"
                Case LogLevel.Success : Return "[OK]"
                Case Else : Return "[BILGI]"
            End Select
        End Get
    End Property

    Public ReadOnly Property LevelColor As String
        Get
            Select Case Level
                Case LogLevel.Error : Return "#FF4757"
                Case LogLevel.Warning : Return "#FFB347"
                Case LogLevel.Success : Return "#00C896"
                Case LogLevel.Command : Return "#1D8CF8"
                Case LogLevel.Response : Return "#7CC5FF"
                Case Else : Return "#8A9BB5"
            End Select
        End Get
    End Property
End Class

Public Class SiteStore
    Private ReadOnly _configPath As String
    Private ReadOnly _dirPath As String

    Public Sub New()
        _dirPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FTPClientPro")

        If Not Directory.Exists(_dirPath) Then
            Directory.CreateDirectory(_dirPath)
        End If

        _configPath = Path.Combine(_dirPath, "sites.json")
    End Sub

    Public Function LoadSites() As List(Of FtpSite)
        Try
            If Not File.Exists(_configPath) Then
                Return New List(Of FtpSite)()
            End If

            Dim json As String = File.ReadAllText(_configPath)
            If String.IsNullOrWhiteSpace(json) Then Return New List(Of FtpSite)()

            Dim ser As New DataContractJsonSerializer(GetType(List(Of FtpSite)))
            Using ms As New MemoryStream(Encoding.UTF8.GetBytes(json))
                Return DirectCast(ser.ReadObject(ms), List(Of FtpSite))
            End Using
        Catch ex As Exception
            Debug.WriteLine("Yükleme Hatası: " & ex.Message)
            Return New List(Of FtpSite)()
        End Try
    End Function

    Public Sub SaveSites(sites As List(Of FtpSite))
        Try
            If sites Is Nothing Then Return

            Dim ser As New DataContractJsonSerializer(GetType(List(Of FtpSite)))
            Using ms As New MemoryStream()
                ser.WriteObject(ms, sites)
                Dim jsonBytes As Byte() = ms.ToArray()
                Dim jsonString As String = Encoding.UTF8.GetString(jsonBytes)

                File.WriteAllText(_configPath, jsonString, Encoding.UTF8)
                Debug.WriteLine("Kaydedilen Konum: " & _configPath)
            End Using
        Catch ex As Exception
            Debug.WriteLine("Kaydetme Hatası: " & ex.Message)
        End Try
    End Sub
End Class

Public Class DriveInfoItem
    Public Property Icon As String
    Public Property DisplayName As String
    Public Property Path As String
End Class
