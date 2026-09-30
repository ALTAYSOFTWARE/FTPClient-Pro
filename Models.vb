Imports System
Imports System.Collections.Generic
Imports System.ComponentModel
Imports System.IO
Imports System.Runtime.Serialization
Imports System.Text
Imports System.Xml.Serialization

Public Enum FtpProtocol
    FTP = 0
    FTPS = 1
    SFTP = 2
End Enum

Public Enum TransferStatus
    Queued
    Transferring
    Completed
    Failed
    Cancelled
End Enum

Public Class FtpServerInfo
    <XmlElement>
    Public Property Name As String = "Yeni Site"
    
    <XmlElement>
    Public Property Host As String = ""
    
    <XmlElement>
    Public Property Port As Integer = 21
    
    <XmlElement>
    Public Property Username As String = ""
    
    <XmlElement>
    Public Property Password As String = ""
    
    <XmlElement>
    Public Property Protocol As FtpProtocol = FtpProtocol.FTP
    
    <XmlElement>
    Public Property Passive As Boolean = True
    
    <XmlElement>
    Public Property Comments As String = ""
End Class

Public Class RemoteFile
    Public Property Name As String
    Public Property FullPath As String
    Public Property IsDirectory As Boolean
    Public Property Size As Long
    Public Property Modified As DateTime
    Public Property Permissions As String
    
    Public ReadOnly Property SizeStr As String
        Get
            If IsDirectory Then Return "-"
            If Size < 1024 Then Return Size.ToString() & " B"
            If Size < 1048576 Then Return String.Format("{0:F1} KB", Size / 1024.0)
            If Size < 1073741824 Then Return String.Format("{0:F1} MB", Size / 1048576.0)
            Return String.Format("{0:F2} GB", Size / 1073741824.0)
        End Get
    End Property
End Class

Public Class LocalFile
    Public Property Name As String
    Public Property FullPath As String
    Public Property IsDirectory As Boolean
    Public Property Size As Long
    Public Property Modified As DateTime
    
    Public ReadOnly Property SizeStr As String
        Get
            If IsDirectory Then Return "-"
            If Size < 1024 Then Return Size.ToString() & " B"
            If Size < 1048576 Then Return String.Format("{0:F1} KB", Size / 1024.0)
            If Size < 1073741824 Then Return String.Format("{0:F1} MB", Size / 1048576.0)
            Return String.Format("{0:F2} GB", Size / 1073741824.0)
        End Get
    End Property
End Class

Public Class TransferItem
    Implements INotifyPropertyChanged
    
    Private _status As TransferStatus
    Private _progress As Double
    Private _speed As Double
    
    Public Property Name As String
    Public Property LocalPath As String
    Public Property RemotePath As String
    Public Property IsUpload As Boolean
    Public Property Size As Long
    Public Property TransferredBytes As Long
    
    Public Property Status As TransferStatus
        Get
            Return _status
        End Get
        Set(value As TransferStatus)
            _status = value
            OnPropertyChanged("Status")
            OnPropertyChanged("StatusText")
        End Set
    End Property
    
    Public Property Progress As Double
        Get
            Return _progress
        End Get
        Set(value As Double)
            _progress = value
            OnPropertyChanged("Progress")
            OnPropertyChanged("ProgressText")
        End Set
    End Property
    
    Public Property Speed As Double
        Get
            Return _speed
        End Get
        Set(value As Double)
            _speed = value
            OnPropertyChanged("Speed")
            OnPropertyChanged("SpeedText")
        End Set
    End Property
    
    Public ReadOnly Property StatusText As String
        Get
            Select Case Status
                Case TransferStatus.Queued : Return "Kuyrukta"
                Case TransferStatus.Transferring : Return "Aktarımda"
                Case TransferStatus.Completed : Return "Tamamlandı"
                Case TransferStatus.Failed : Return "Hata"
                Case TransferStatus.Cancelled : Return "İptal"
                Case Else : Return "Unknown"
            End Select
        End Get
    End Property
    
    Public ReadOnly Property ProgressText As String
        Get
            Return String.Format("{0:F1}%", Progress)
        End Get
    End Property
    
    Public ReadOnly Property SpeedText As String
        Get
            If Speed <= 0 Then Return "-"
            If Speed < 1024 Then Return String.Format("{0:F0} B/s", Speed)
            If Speed < 1048576 Then Return String.Format("{0:F1} KB/s", Speed / 1024.0)
            Return String.Format("{0:F1} MB/s", Speed / 1048576.0)
        End Get
    End Property
    
    Public Event PropertyChanged As PropertyChangedEventHandler Implements INotifyPropertyChanged.PropertyChanged
    
    Protected Sub OnPropertyChanged(name As String)
        RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(name))
    End Sub
End Class