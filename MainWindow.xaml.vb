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
    
    Private Async Sub Connect()
        Try
            If String.IsNullOrWhiteSpace(txtHost.Text) Then
                MessageBox.Show("Host adresini girin", "Hata", MessageBoxButton.OK, MessageBoxImage.Error)
                Return
            End If
            
            Dim port As Integer = 21
            Integer.TryParse(txtPort.Text, port)
            
            AddLog(String.Format("Bağlanıyor: {0}:{1}", txtHost.Text, port))
            UpdateStatus("Bağlanıyor...")
            
            _ftpEngine = New FtpEngine(txtHost.Text, port, "", "", True)
            AddHandler _ftpEngine.LogMessage, AddressOf OnFtpLog
            
            Dim success = Await _ftpEngine.TestConnectionAsync()
            
            If success Then
                _connected = True
                UpdateStatus("Bağlı")
                Await RefreshRemoteAsync()
            Else
                _connected = False
                UpdateStatus("Bağlantı başarısız")
            End If
            
        Catch ex As Exception
            AddLog("Bağlantı hatası: " & ex.Message)
        End Try
    End Sub
    
    Private Async Function RefreshRemoteAsync() As Task
        Try
            If Not _connected OrElse _ftpEngine Is Nothing Then
                AddLog("Bağlı değilsiniz")
                Return
            End If
            
            _remoteFiles.Clear()
            AddLog("Uzak dosyalar listeleniyor...")
            
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
            
            AddLog(String.Format("Uzak klasör yüklendi: {0} dosya", files.Count))
            
        Catch ex As Exception
            AddLog("Uzak listeleme hatası: " & ex.Message)
        End Try
    End Function
    
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