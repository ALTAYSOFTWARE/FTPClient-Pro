Imports System
Imports System.IO
Imports System.Net
Imports System.Collections.ObjectModel
Imports System.Windows
Imports System.Windows.Controls

Class MainWindow
    Private _localPath As String = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
    Private _remotePath As String = "/"
    Private _localFiles As New ObservableCollection(Of LocalFile)
    Private _remoteFiles As New ObservableCollection(Of RemoteFile)
    Private _transfers As New ObservableCollection(Of TransferItem)
    Private _logs As New ObservableCollection(Of String)
    Private _ftpClient As FtpWebRequest
    Private _connected As Boolean = False
    
    Public Sub New()
        InitializeComponent()
        lstLocalFiles.ItemsSource = _localFiles
        lstRemoteFiles.ItemsSource = _remoteFiles
        lstTransfers.ItemsSource = _transfers
        lstLog.ItemsSource = _logs
        
        AddLog("Başlangıç tamamlandı...")
        RefreshLocal()
    End Sub
    
    Private Sub RefreshLocal()
        Try
            _localFiles.Clear()
            
            ' Parent klasörü ekle
            Dim parent = Directory.GetParent(_localPath)
            If parent IsNot Nothing Then
                _localFiles.Add(New LocalFile With {
                    .Name = "..",
                    .FullPath = parent.FullName,
                    .IsDirectory = True
                })
            End If
            
            ' Klasörleri ekle
            For Each dir In Directory.GetDirectories(_localPath)
                _localFiles.Add(New LocalFile With {
                    .Name = Path.GetFileName(dir),
                    .FullPath = dir,
                    .IsDirectory = True
                })
            Next
            
            ' Dosyaları ekle
            For Each file In Directory.GetFiles(_localPath)
                Dim fi = New FileInfo(file)
                _localFiles.Add(New LocalFile With {
                    .Name = fi.Name,
                    .FullPath = fi.FullName,
                    .IsDirectory = False,
                    .Size = fi.Length,
                    .Modified = fi.LastWriteTime
                })
            Next
            
            AddLog(String.Format("Yerel klasör yüklendi: {0}", _localPath))
        Catch ex As Exception
            AddLog("Hata: " & ex.Message)
        End Try
    End Sub
    
    Private Sub RefreshRemote()
        Try
            _remoteFiles.Clear()
            AddLog("Uzak dosyalar listeleniyor...")
            ' TODO: FTP bağlantısı üzerinden listeleme yapılacak
        Catch ex As Exception
            AddLog("Uzak listeleme hatası: " & ex.Message)
        End Try
    End Sub
    
    Private Sub AddLog(message As String)
        Dispatcher.BeginInvoke(Sub()
            _logs.Add(String.Format("[{0:HH:mm:ss}] {1}", DateTime.Now, message))
            If _logs.Count > 100 Then
                _logs.RemoveAt(0)
            End If
            lstLog.ScrollIntoView(lstLog.Items(lstLog.Items.Count - 1))
        End Sub)
    End Sub
End Class