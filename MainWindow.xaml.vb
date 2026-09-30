Imports System
Imports System.IO
Imports System.Collections.ObjectModel
Imports System.Windows
Imports System.Windows.Controls
Imports System.Threading.Tasks

Class MainWindow
    Private _localPath As String = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
    Private _remotePath As String = "/"
    Private _localFiles As New ObservableCollection(Of LocalFile)
    Private _remoteFiles As New ObservableCollection(Of RemoteFile)
    Private _transfers As New ObservableCollection(Of TransferItem)
    Private _logs As New ObservableCollection(Of String)
    Private _ftpEngine As FtpEngine
    Private _connected As Boolean = False
    
    Public Sub New()
        InitializeComponent()
        lstLocalFiles.ItemsSource = _localFiles
        lstRemoteFiles.ItemsSource = _remoteFiles
        lstTransfers.ItemsSource = _transfers
        lstLog.ItemsSource = _logs
        
        AddLog("Başlangıç tamamlandı...")
        RefreshLocal()
        UpdateLocalPath()
    End Sub
    
    Private Sub UpdateLocalPath()
        lblLocalPath.Text = "Yol: " & _localPath
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
                    .IsDirectory = True,
                    .Modified = DateTime.Now,
                    .Size = 0
                })
            End If
            
            ' Klasörleri ekle
            For Each dir In Directory.GetDirectories(_localPath)
                Try
                    _localFiles.Add(New LocalFile With {
                        .Name = Path.GetFileName(dir),
                        .FullPath = dir,
                        .IsDirectory = True,
                        .Modified = Directory.GetLastWriteTime(dir),
                        .Size = 0
                    })
                Catch
                End Try
            Next
            
            ' Dosyaları ekle
            For Each file In Directory.GetFiles(_localPath)
                Try
                    Dim fi = New FileInfo(file)
                    _localFiles.Add(New LocalFile With {
                        .Name = fi.Name,
                        .FullPath = fi.FullName,
                        .IsDirectory = False,
                        .Size = fi.Length,
                        .Modified = fi.LastWriteTime
                    })
                Catch
                End Try
            Next
            
            AddLog(String.Format("Yerel klasör yüklendi: {0}", _localPath))
            UpdateStatus("Yerel klasör hazır")
            
        Catch ex As Exception
            AddLog("Hata: " & ex.Message)
        End Try
    End Sub
    
    Private Async Sub btnConnect_Click(sender As Object, e As RoutedEventArgs)
        Try
            If String.IsNullOrWhiteSpace(txtHost.Text) Then
                MessageBox.Show("Host adresini girin", "Hata", MessageBoxButton.OK, MessageBoxImage.Error)
                Return
            End If
            
            Dim port As Integer = 21
            Integer.TryParse(txtPort.Text, port)
            
            Dim username As String = If(String.IsNullOrEmpty(txtUsername.Text), "anonymous", txtUsername.Text)
            Dim password As String = If(String.IsNullOrEmpty(pbPassword.Password), "anonymous@", pbPassword.Password)
            
            AddLog(String.Format("Bağlanıyor: {0}:{1}", txtHost.Text, port))
            UpdateStatus("Bağlanıyor...")
            
            _ftpEngine = New FtpEngine(txtHost.Text, port, username, password, True)
            AddHandler _ftpEngine.LogMessage, AddressOf OnFtpLog
            
            Dim success = Await _ftpEngine.TestConnectionAsync()
            
            If success Then
                _connected = True
                UpdateStatus("Bağlı")
                btnConnect.IsEnabled = False
                btnDisconnect.IsEnabled = True
                btnDownload.IsEnabled = True
                btnRemoteDelete.IsEnabled = True
                btnRefreshRemote.IsEnabled = True
                Await RefreshRemoteAsync()
            Else
                _connected = False
                UpdateStatus("Bağlantı başarısız")
            End If
            
        Catch ex As Exception
            AddLog("Bağlantı hatası: " & ex.Message)
        End Try
    End Sub
    
    Private Sub btnDisconnect_Click(sender As Object, e As RoutedEventArgs)
        _connected = False
        _ftpEngine = Nothing
        _remoteFiles.Clear()
        UpdateStatus("Kesildi")
        btnConnect.IsEnabled = True
        btnDisconnect.IsEnabled = False
        btnDownload.IsEnabled = False
        btnRemoteDelete.IsEnabled = False
        btnRefreshRemote.IsEnabled = False
        AddLog("Bağlantı kesildi")
    End Sub
    
    Private Async Sub btnRefreshRemote_Click(sender As Object, e As RoutedEventArgs)
        If _connected Then
            Await RefreshRemoteAsync()
        End If
    End Sub
    
    Private Async Function RefreshRemoteAsync() As Task
        Try
            If Not _connected OrElse _ftpEngine Is Nothing Then
                AddLog("Bağlı değilsiniz")
                Return
            End If
            
            _remoteFiles.Clear()
            AddLog("Uzak dosyalar listeleniyor...")
            UpdateStatus("Listeleniyor...")
            
            Dim files = Await _ftpEngine.ListDirectoryAsync(_remotePath)
            
            ' Parent ekle
            If _remotePath <> "/" Then
                _remoteFiles.Add(New RemoteFile With {
                    .Name = "..",
                    .FullPath = "/",
                    .IsDirectory = True,
                    .Size = 0,
                    .Modified = DateTime.Now
                })
            End If
            
            ' Dosyaları ekle
            For Each file In files
                _remoteFiles.Add(file)
            Next
            
            lblRemotePath.Text = "Yol: " & _remotePath
            AddLog(String.Format("Uzak klasör yüklendi: {0} dosya", files.Count))
            UpdateStatus("Hazır")
            
        Catch ex As Exception
            AddLog("Uzak listeleme hatası: " & ex.Message)
            UpdateStatus("Hata")
        End Try
    End Function
    
    Private Async Sub btnDownload_Click(sender As Object, e As RoutedEventArgs)
        If Not _connected Then Return
        
        Dim selectedFiles = lstRemoteFiles.SelectedItems
        If selectedFiles.Count = 0 Then
            MessageBox.Show("Dosya seçin", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Information)
            Return
        End If
        
        For Each item In selectedFiles
            Dim remoteFile = DirectCast(item, RemoteFile)
            If remoteFile.Name = ".." Then Continue For
            
            If remoteFile.IsDirectory Then
                AddLog("Klasör indirmesi henüz desteklenmiyor: " & remoteFile.Name)
                Continue For
            End If
            
            Dim localPath = Path.Combine(_localPath, remoteFile.Name)
            Dim transfer = New TransferItem With {
                .Name = remoteFile.Name,
                .LocalPath = localPath,
                .RemotePath = remoteFile.FullPath,
                .IsUpload = False,
                .Size = remoteFile.Size,
                .Status = TransferStatus.Queued
            }
            
            _transfers.Add(transfer)
            
            Dim success = Await _ftpEngine.DownloadFileAsync(remoteFile.FullPath, localPath)
            transfer.Status = If(success, TransferStatus.Completed, TransferStatus.Failed)
            transfer.Progress = If(success, 100, 0)
        Next
        
        RefreshLocal()
        Await RefreshRemoteAsync()
    End Sub
    
    Private Async Sub btnUpload_Click(sender As Object, e As RoutedEventArgs)
        If Not _connected Then Return
        
        Dim selectedFiles = lstLocalFiles.SelectedItems
        If selectedFiles.Count = 0 Then
            MessageBox.Show("Dosya seçin", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Information)
            Return
        End If
        
        For Each item In selectedFiles
            Dim localFile = DirectCast(item, LocalFile)
            If localFile.Name = ".." Then Continue For
            
            If localFile.IsDirectory Then
                AddLog("Klasör yüklemesi henüz desteklenmiyor: " & localFile.Name)
                Continue For
            End If
            
            Dim remotePath = _remotePath.TrimEnd("/"c) & "/" & localFile.Name
            Dim transfer = New TransferItem With {
                .Name = localFile.Name,
                .LocalPath = localFile.FullPath,
                .RemotePath = remotePath,
                .IsUpload = True,
                .Size = localFile.Size,
                .Status = TransferStatus.Queued
            }
            
            _transfers.Add(transfer)
            
            Dim success = Await _ftpEngine.UploadFileAsync(localFile.FullPath, remotePath)
            transfer.Status = If(success, TransferStatus.Completed, TransferStatus.Failed)
            transfer.Progress = If(success, 100, 0)
        Next
        
        Await RefreshRemoteAsync()
    End Sub
    
    Private Async Sub btnLocalDelete_Click(sender As Object, e As RoutedEventArgs)
        Dim selectedFiles = lstLocalFiles.SelectedItems
        If selectedFiles.Count = 0 Then Return
        
        If MessageBox.Show(String.Format("{0} dosya silinecek. Emin misiniz?", selectedFiles.Count), "Sil", MessageBoxButton.YesNo, MessageBoxImage.Question) = MessageBoxResult.No Then
            Return
        End If
        
        For Each item In selectedFiles
            Dim localFile = DirectCast(item, LocalFile)
            If localFile.Name = ".." Then Continue For
            
            Try
                If localFile.IsDirectory Then
                    Directory.Delete(localFile.FullPath, True)
                Else
                    File.Delete(localFile.FullPath)
                End If
                AddLog("Silindi: " & localFile.Name)
            Catch ex As Exception
                AddLog("Silme hatası: " & ex.Message)
            End Try
        Next
        
        RefreshLocal()
    End Sub
    
    Private Async Sub btnRemoteDelete_Click(sender As Object, e As RoutedEventArgs)
        If Not _connected Then Return
        
        Dim selectedFiles = lstRemoteFiles.SelectedItems
        If selectedFiles.Count = 0 Then Return
        
        If MessageBox.Show(String.Format("{0} dosya silinecek. Emin misiniz?", selectedFiles.Count), "Sil", MessageBoxButton.YesNo, MessageBoxImage.Question) = MessageBoxResult.No Then
            Return
        End If
        
        For Each item In selectedFiles
            Dim remoteFile = DirectCast(item, RemoteFile)
            If remoteFile.Name = ".." Then Continue For
            
            Dim success = Await _ftpEngine.DeleteFileAsync(remoteFile.FullPath)
            If success Then
                AddLog("Silindi: " & remoteFile.Name)
            End If
        Next
        
        Await RefreshRemoteAsync()
    End Sub
    
    Private Sub lstLocalFiles_MouseDoubleClick(sender As Object, e As System.Windows.Input.MouseButtonEventArgs)
        Dim item = DirectCast(lstLocalFiles.SelectedItem, LocalFile)
        If item Is Nothing Then Return
        
        If item.IsDirectory Then
            _localPath = item.FullPath
            RefreshLocal()
        End If
    End Sub
    
    Private Async Sub lstRemoteFiles_MouseDoubleClick(sender As Object, e As System.Windows.Input.MouseButtonEventArgs)
        If Not _connected Then Return
        
        Dim item = DirectCast(lstRemoteFiles.SelectedItem, RemoteFile)
        If item Is Nothing Then Return
        
        If item.IsDirectory Then
            _remotePath = item.FullPath
            Await RefreshRemoteAsync()
        End If
    End Sub
    
    Private Sub btnRefreshLocal_Click(sender As Object, e As RoutedEventArgs)
        RefreshLocal()
    End Sub
    
    Private Sub OnFtpLog(message As String)
        AddLog(message)
    End Sub
    
    Private Sub AddLog(message As String)
        Dispatcher.BeginInvoke(Sub()
            _logs.Add(String.Format("[{0:HH:mm:ss}] {1}", DateTime.Now, message))
            If _logs.Count > 100 Then
                _logs.RemoveAt(0)
            End If
            Try
                lstLog.ScrollIntoView(lstLog.Items(lstLog.Items.Count - 1))
            Catch
            End Try
        End Sub)
    End Sub
    
    Private Sub UpdateStatus(message As String)
        Dispatcher.BeginInvoke(Sub()
            lblStatus.Text = message
        End Sub)
    End Sub
End Class