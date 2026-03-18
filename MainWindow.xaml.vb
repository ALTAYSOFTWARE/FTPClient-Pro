Imports System
Imports System.Collections.Generic
Imports System.Collections.ObjectModel
Imports System.Diagnostics
Imports System.IO
Imports System.Linq
Imports System.Net
Imports System.Security.Cryptography
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows
Imports System.Windows.Controls
Imports System.Windows.Data
Imports System.Windows.Input
Imports System.Windows.Media

Partial Public Class MainWindow
    Inherits Window

    Private _engine As FtpEngine
    Private _site As FtpSite
    Private _remoteDir As String = "/"
    Private _localDir As String = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
    Private ReadOnly _localFiles As New ObservableCollection(Of LocalFileItem)()
    Private ReadOnly _remoteFiles As New ObservableCollection(Of FtpFileItem)()
    Private ReadOnly _transfers As New ObservableCollection(Of TransferItem)()
    Private ReadOnly _logs As New ObservableCollection(Of LogEntry)()
    Private ReadOnly _store As New SiteStore()
    Private ReadOnly _cancelSources As New Dictionary(Of String, CancellationTokenSource)()
    Private _connected As Boolean = False
    Private _isDarkMode As Boolean = False
    Private _logRowHeight As GridLength = New GridLength(1.2, GridUnitType.Star)
    ' Arka planda açık olan dosyaları dinlemek için
    Private ReadOnly _fileWatchers As New Dictionary(Of String, IO.FileSystemWatcher)()
    Private _lastUploadTime As DateTime = DateTime.MinValue ' Ardışık çoklu kaydetmeleri engellemek için
    Private _transferRowHeight As GridLength = New GridLength(2, GridUnitType.Star)
    Private WithEvents _queueTimer As New System.Windows.Threading.DispatcherTimer()
    Private _lastTransferCount As Integer = 0
    ' Bağlantıyı hayatta tutacak gizli zamanlayıcı
    Private WithEvents _keepAliveTimer As New System.Windows.Threading.DispatcherTimer()
    ' Paralel transferleri kontrol eden trafik polisi (Semaphore)
    Private _transferSemaphore As Threading.SemaphoreSlim
    Private _activeTransfers As New List(Of Task)
    Private _history As New List(Of FtpSite)()
    Private Const HistoryFile As String = "history.dat"
    Private _isProcessing As Boolean = False
    Private WithEvents _trayIcon As System.Windows.Forms.NotifyIcon
    Public Sub New()
        InitializeComponent()
        ApplyTheme()
        dgLocalFiles.ItemsSource = _localFiles
        dgRemoteFiles.ItemsSource = _remoteFiles
        dgTransfers.ItemsSource = _transfers
        lstLog.ItemsSource = _logs
        _trayIcon = New System.Windows.Forms.NotifyIcon()
        _trayIcon.Icon = System.Drawing.SystemIcons.Application
        _trayIcon.Text = "FTP Client Pro"
        _trayIcon.Visible = True
        ' HATALI OLAN KISMI BU TEMİZ HALİYLE DEĞİŞTİR:
        AddHandler _transfers.CollectionChanged,
    Sub(sender, e)
        If e.Action = System.Collections.Specialized.NotifyCollectionChangedAction.Add Then
            Dim _ign = Task.Run(Function() ProcessQueueAsync())
        End If
    End Sub

        LoadLocalDrives()
        LoadLocal(_localDir)

        _queueTimer.Interval = TimeSpan.FromMilliseconds(500)
        _queueTimer.Start()

        _keepAliveTimer.Interval = TimeSpan.FromSeconds(45)
        _keepAliveTimer.Start()

        Log("Hazir. Baglanmak icin Site Yoneticisi'ni kullanın.", LogLevel.Info)
    End Sub
    Protected Overrides Sub OnStateChanged(e As EventArgs)
        If WindowState = WindowState.Minimized Then
            Me.Hide()
            _trayIcon.ShowBalloonTip(2000, "Arka Planda Çalışıyor", "Transferleriniz devam ediyor.", System.Windows.Forms.ToolTipIcon.Info)
        End If
        MyBase.OnStateChanged(e)
    End Sub

    Private Sub _trayIcon_DoubleClick(sender As Object, e As EventArgs) Handles _trayIcon.DoubleClick
        Me.Show()
        Me.WindowState = WindowState.Normal
        Me.Activate()
    End Sub
    Private Async Sub _keepAliveTimer_Tick(sender As Object, e As EventArgs) Handles _keepAliveTimer.Tick
        ' Sadece bağlıysak ve motor aktifse hayatta kalma sinyali gönder
        If _connected AndAlso _engine IsNot Nothing Then
            Await _engine.KeepAliveAsync()
        End If
    End Sub
    Private Sub _queueTimer_Tick(sender As Object, e As EventArgs) Handles _queueTimer.Tick
        Dim activeCount As Integer = 0
        Dim completedCount As Integer = 0
        Dim queuedCount As Integer = 0

        ' Tablodaki (dgTransfers) tüm öğeleri tarıyoruz
        For Each item In dgTransfers.Items
            Dim t As TransferItem = TryCast(item, TransferItem)
            If t IsNot Nothing Then
                ' Durumu "Aktarımda" veya "Bağlanıyor" olanlar aktiftir
                If t.Status = TransferStatus.Transferring OrElse t.Status = TransferStatus.Connecting Then
                    activeCount += 1

                    ' Durumu "Tamamlandı" olanlar
                ElseIf t.Status = TransferStatus.Completed Then
                    completedCount += 1

                    ' İptal edilenler veya hata verenler dışındakiler beklemededir (Kuyruk)
                ElseIf t.Status <> TransferStatus.Cancelled AndAlso t.Status <> TransferStatus.Failed Then
                    queuedCount += 1
                End If
            End If
        Next

        ' Arayüzdeki (XAML) lblQueueStats metnini canlı olarak güncelliyoruz
        lblQueueStats.Text = $"{activeCount} Aktif {queuedCount} Kuyruk {completedCount} Tamam"
        If chkTransferAutoScroll IsNot Nothing AndAlso chkTransferAutoScroll.IsChecked = True Then
            ' Eğer kuyruktaki dosya sayısı, bizim hatırladığımız sayıdan fazlaysa (yeni dosya eklendiyse)
            If dgTransfers.Items.Count > 0 AndAlso dgTransfers.Items.Count > _lastTransferCount Then
                ' En son eklenen öğeye pürüzsüzce kaydır
                dgTransfers.ScrollIntoView(dgTransfers.Items(dgTransfers.Items.Count - 1))
            End If
        End If

        ' Sayıyı hafızaya al ki bir sonraki turda tekrar tekrar kaydırıp kullanıcıyı deli etmesin
        _lastTransferCount = dgTransfers.Items.Count
    End Sub
    ' ====== HELPERS ======
    Private Shared Function ParentPath(path As String) As String
        Dim t As String = path.TrimEnd("/"c)
        Dim idx As Integer = t.LastIndexOf("/"c)
        If idx <= 0 Then Return "/"
        Return t.Substring(0, idx)
    End Function

    Private Shared Function FmtBytes(bytes As Long) As String
        If bytes < 1024L Then Return bytes.ToString() & " B"
        If bytes < 1048576L Then Return String.Format("{0:F1} KB", bytes / 1024.0)
        If bytes < 1073741824L Then Return String.Format("{0:F1} MB", bytes / 1048576.0)
        Return String.Format("{0:F2} GB", bytes / 1073741824.0)
    End Function

    Private Sub Log(msg As String, lvl As LogLevel)
        ' UI Thread'ine güvenli geçiş yapıyoruz
        Dispatcher.BeginInvoke(Sub()
                                   Try
                                       Dim e As New LogEntry() With {.Message = msg, .Level = lvl}
                                       _logs.Add(e)

                                       ' Log sayısını sınırlayalım (Bellek şişmesin)
                                       While _logs.Count > 1000
                                           _logs.RemoveAt(0)
                                       End While

                                       ' Otomatik kaydırma
                                       If chkAutoScroll IsNot Nothing AndAlso chkAutoScroll.IsChecked = True AndAlso _logs.Count > 0 Then
                                           lstLog.ScrollIntoView(_logs(_logs.Count - 1))
                                       End If
                                   Catch ex As Exception
                                       ' Loglama sırasında hata oluşursa uygulamayı çökertme
                                   End Try
                               End Sub)
    End Sub

    Private Sub SetStatus(msg As String)
        Dispatcher.BeginInvoke(Sub() lblStatus.Text = msg)
    End Sub

    Private Sub UpdateQueueStats()
        Dim q As Integer = Enumerable.Count(_transfers, Function(t) t.Status = TransferStatus.Queued)
        Dim a As Integer = Enumerable.Count(_transfers, Function(t) t.IsActive)
        Dim d As Integer = Enumerable.Count(_transfers, Function(t) t.Status = TransferStatus.Completed)
        lblQueueStats.Text = String.Format("{0} aktif  {1} kuyruk  {2} tamam", a, q, d)

    End Sub

    Private Function GetLocalSelected() As List(Of LocalFileItem)
        Return Enumerable.ToList(Enumerable.Cast(Of LocalFileItem)(dgLocalFiles.SelectedItems))
    End Function

    Private Function GetRemoteSelected() As List(Of FtpFileItem)
        Return Enumerable.ToList(Enumerable.Cast(Of FtpFileItem)(dgRemoteFiles.SelectedItems))
    End Function

    Private Function AskInput(prompt As String, title As String, def As String) As String
        Dim dlg As New InputBoxWindow(prompt, title, def)
        dlg.Owner = Me
        If dlg.ShowDialog() = True Then Return dlg.Result
        Return Nothing
    End Function

    ' ====== LOCAL FILE SYSTEM ======
    Private Sub LoadLocalDrives()
        Dim items As New List(Of DriveInfoItem)()

        ' 1. MASAÜSTÜ KISAYOLU
        Dim desktopPath As String = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
        items.Add(New DriveInfoItem With {
            .Icon = "💻",
            .DisplayName = "Masaüstü",
            .Path = desktopPath
        })

        ' 2. İNDİRİLENLER KISAYOLU (En çok aranan klasör)
        Dim downloadsPath As String = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
        If Directory.Exists(downloadsPath) Then
            items.Add(New DriveInfoItem With {
                .Icon = "⬇️",
                .DisplayName = "İndirilenler",
                .Path = downloadsPath
            })
        End If

        ' 3. BELGELER KISAYOLU
        Dim docsPath As String = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        items.Add(New DriveInfoItem With {
            .Icon = "📄",
            .DisplayName = "Belgeler",
            .Path = docsPath
        })

        ' 4. KULLANICI PROFİLİ (Ana dizin)
        Dim userPath As String = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        items.Add(New DriveInfoItem With {
            .Icon = "👤",
            .DisplayName = "Kullanıcı",
            .Path = userPath
        })

        ' 5. BİLGİSAYARDAKİ SÜRÜCÜLER (C:, D: vb.)
        For Each drv As System.IO.DriveInfo In System.IO.DriveInfo.GetDrives()
            Try
                If drv.IsReady Then
                    Dim volLabel As String = If(String.IsNullOrWhiteSpace(drv.VolumeLabel), "Yerel Disk", drv.VolumeLabel)
                    items.Add(New DriveInfoItem With {
                        .Icon = "🖴",
                        .DisplayName = volLabel & " (" & drv.Name.TrimEnd("\"c) & ")",
                        .Path = drv.RootDirectory.FullName
                    })
                End If
            Catch
            End Try
        Next

        ' Listeyi ComboBox'a bağla
        cmbLocalDrives.ItemsSource = items

        ' UI açıldığında kutu boş görünmesin, doğrudan Masaüstü (veya ilk sıradaki) seçili gelsin
        If cmbLocalDrives.Items.Count > 0 AndAlso cmbLocalDrives.SelectedIndex = -1 Then
            cmbLocalDrives.SelectedIndex = 0
        End If
    End Sub

    ' ComboBox'tan yeni bir yer seçildiğinde tetiklenir
    Private Sub cmbLocalDrives_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)
        If cmbLocalDrives.SelectedItem IsNot Nothing Then
            Dim selectedItem As DriveInfoItem = DirectCast(cmbLocalDrives.SelectedItem, DriveInfoItem)

            ' Seçilen yere git
            If _localDir <> selectedItem.Path Then
                LoadLocal(selectedItem.Path)
            End If
        End If
    End Sub

    Private Sub LoadLocal(path As String)
        Try
            If Not Directory.Exists(path) Then Return
            _localDir = path
            _localFiles.Clear()

            Dim parent As DirectoryInfo = Directory.GetParent(path)
            If parent IsNot Nothing Then
                _localFiles.Add(New LocalFileItem With {.Name = "..", .FullPath = parent.FullName, .IsDirectory = True, .LastModified = DateTime.Now})
            End If

            For Each d As String In Directory.GetDirectories(path)
                Try
                    Dim di As New DirectoryInfo(d)
                    _localFiles.Add(New LocalFileItem With {.Name = di.Name, .FullPath = di.FullName, .IsDirectory = True, .LastModified = di.LastWriteTime})
                Catch
                End Try
            Next

            Dim totalSizeBytes As Long = 0 ' Toplam boyutu tutacağımız değişken

            For Each f As String In Directory.GetFiles(path)
                Try
                    Dim fi As New FileInfo(f)
                    _localFiles.Add(New LocalFileItem With {
                        .Name = fi.Name, .FullPath = fi.FullName, .IsDirectory = False,
                        .Size = fi.Length, .LastModified = fi.LastWriteTime, .Extension = fi.Extension
                    })
                    totalSizeBytes += fi.Length ' Dosyanın boyutunu genel toplama ekle
                Catch
                End Try
            Next

            Dim dirs As Integer = Enumerable.Count(_localFiles, Function(x) x.IsDirectory AndAlso x.Name <> "..")
            Dim files As Integer = Enumerable.Count(_localFiles, Function(x) Not x.IsDirectory)

            ' İstediğin modern görünümlü detaylı alt bilgi (N0 = binlik ayraçlı format)
            lblLocalInfo.Text = $"{files} dosya ve {dirs} klasör. Toplam boyut: {totalSizeBytes:N0} bayt"

        Catch ex As UnauthorizedAccessException
            Log("Erişim reddedildi: " & path, LogLevel.Warning)
        Catch ex As Exception
            Log("Yerel hata: " & ex.Message, LogLevel.Error)
        End Try
    End Sub

    ' ====== REMOTE FILE SYSTEM ======
    Private Async Function LoadRemote(path As String) As Task
        If Not _connected OrElse _engine Is Nothing Then Return
        Try
            SetStatus("Listeleniyor: " & path)
            Dim items As List(Of FtpFileItem) = Await _engine.ListDirectoryAsync(path)
            Dispatcher.Invoke(Sub()
                                  _remoteDir = path
                                  txtRemotePath.Text = path
                                  _remoteFiles.Clear()
                                  If path <> "/" AndAlso path.Length > 1 Then
                                      _remoteFiles.Add(New FtpFileItem With {
                        .Name = "..",
                        .FullPath = ParentPath(path),
                        .IsDirectory = True
                    })
                                  End If
                                  For Each item As FtpFileItem In Enumerable.Where(items, Function(i) i.IsDirectory).OrderBy(Function(i) i.Name)
                                      _remoteFiles.Add(item)
                                  Next
                                  For Each item As FtpFileItem In Enumerable.Where(items, Function(i) Not i.IsDirectory).OrderBy(Function(i) i.Name)
                                      _remoteFiles.Add(item)
                                  Next
                                  Dim dirs As Integer = Enumerable.Count(items, Function(i) i.IsDirectory)
                                  Dim files As Integer = Enumerable.Count(items, Function(i) Not i.IsDirectory)
                                  lblRemoteInfo.Text = String.Format("{0} klasor, {1} dosya  |  {2}",
                    dirs, files, FmtBytes(Enumerable.Where(items, Function(i) Not i.IsDirectory).Sum(Function(i) i.Size)))
                              End Sub)
            SetStatus("Hazir  |  " & path)
            txtRemoteSearch.IsEnabled = True
        Catch ex As Exception
            Log("Uzak listeleme hatası: " & ex.Message, LogLevel.Error)
            SetStatus("Hata")
        End Try
    End Function

    ' ====== CONNECT / DISCONNECT ======
    Private Async Sub ConnectTo(s As FtpSite)
        Try
            ' EKLENEN KISIM: Site Yöneticisinden gelen bilgileri ana ekrandaki kutulara doldur
            txtHost.Text = s.Host
            txtUsername.Text = s.Username
            txtPassword.Password = s.Password
            txtPort.Text = s.Port.ToString()

            ' Bağlantı sürecinde butonlara tekrar basılmasını engelle
            SetConnected(False)
            btnConnect.IsEnabled = False

            If _connected Then Disconnect()
            _site = s
            _engine = New FtpEngine(s)

            ' Log olayını bağla
            AddHandler _engine.LogMessage, Sub(msg, lvl) Dispatcher.BeginInvoke(Sub() Log(msg, lvl))

            Log("Bağlantı başlatılıyor: " & s.Host, LogLevel.Info)
            SetStatus("Bağlanıyor...")

            ' Bağlantı testini donmadan (Task.Run ile) arka planda yap
            Dim ok As Boolean = Await Task.Run(Function() _engine.TestConnectionAsync())

            If ok Then
                _connected = True
                SetConnected(True)
                Log("Bağlantı başarılı.", LogLevel.Success)

                ' Bağlandıktan sonra uzak sunucu klasör listesini çek
                Await LoadRemote(If(String.IsNullOrEmpty(s.RemotePath), "/", s.RemotePath))
                btnCompare.IsEnabled = True
                AddToHistory(_site)
            Else
                _connected = False
                SetConnected(False)
                MessageBox.Show("Sunucuya bağlanılamadı. Lütfen bilgileri kontrol edin.", "Bağlantı Hatası", MessageBoxButton.OK, MessageBoxImage.Error)
                btnCompare.IsEnabled = False
            End If
        Catch ex As Exception
            _connected = False
            SetConnected(False)
            Log("Bağlantı hatası: " & ex.Message, LogLevel.Error)
            MessageBox.Show("Bağlantı sırasında bir hata oluştu: " & ex.Message, "Hata", MessageBoxButton.OK, MessageBoxImage.Error)
        Finally
            ' Hata olsa da olmasa da Bağlan butonunun durumunu güncelle
            If Not _connected Then btnConnect.IsEnabled = True
        End Try
    End Sub

    Private Sub Disconnect()
        _engine?.Dispose()
        _engine = Nothing
        _connected = False
        _remoteFiles.Clear()
        SetConnected(False)
        lblRemoteInfo.Text = "Bagli Degil"
        txtRemotePath.Text = "/"
        Log("Baglantı kesildi.", LogLevel.Info)
        SetStatus("Baglantı kesildi")
        txtRemoteSearch.IsEnabled = False
        txtRemoteSearch.Clear() ' İçindeki eski aramayı da temizleyelim
        btnCompare.IsEnabled = False
    End Sub

    Private Sub SetConnected(c As Boolean)
        _connected = c
        btnConnect.IsEnabled = Not c
        btnDisconnect.IsEnabled = c
        mnuConnect.IsEnabled = Not c
        mnuDisconnect.IsEnabled = c
        btnRefresh.IsEnabled = c
        'btnDownload.IsEnabled = c
        'btnUpload.IsEnabled = c
        'btnMkdir.IsEnabled = c
        'btnDelete.IsEnabled = c
        btnRemoteParent.IsEnabled = c
        btnRemoteRefresh.IsEnabled = c
        btnRemoteMkdir.IsEnabled = c
        btnRemoteGo.IsEnabled = c
        txtRemotePath.IsEnabled = c
        ellConn.Fill = If(c,
            New SolidColorBrush(DirectCast(ColorConverter.ConvertFromString("#00C896"), Color)),
            New SolidColorBrush(DirectCast(ColorConverter.ConvertFromString("#FF4757"), Color)))
        lblRemoteHeader.Text = If(c AndAlso _site IsNot Nothing,
            "Uzak Sistem [" & _site.Host & ":" & _site.Port.ToString() & "]",
            "Uzak Sistem [Bagli Degil]")
    End Sub



    ' ====== TRANSFERS ======
    Private Sub EnqueueDownload(items As IEnumerable(Of FtpFileItem))
        If Not _connected Then Return

        For Each item As FtpFileItem In items
            If item.Name = ".." Then Continue For

            Dim remote As String = _remoteDir.TrimEnd("/"c) & "/" & item.Name
            Dim local As String = Path.Combine(_localDir, item.Name)

            If item.IsDirectory Then
                ' Klasör indirme: Arka planda klasörü tarayıp dosyaları kuyruğa ekler
                Dim _ign2 As Task = Task.Run(Async Function()
                                                 Await DownloadDirAsync(remote, local, CancellationToken.None)
                                                 Dispatcher.BeginInvoke(Sub() LoadLocal(_localDir))

                                                 ' ÖNEMLİ EKLEME: Klasör taraması bitince kuyruk işleyiciyi tekrar dürtüyoruz
                                                 Await ProcessQueueAsync()
                                             End Function)
            Else
                ' Dosya indirme: Doğrudan kuyruğa ekle
                Dim t As New TransferItem With {
                .FileName = item.Name,
                .LocalPath = local,
                .RemotePath = remote,
                .FileSize = item.Size,
                .Direction = TransferDirection.Download,
                .Site = _site,
                .Status = TransferStatus.Queued ' DURUMU KUYRUKTA YAPTIK
            }

                _transfers.Add(t)
                ' NOT: Eski sistemdeki RunTransfer(t) satırı tamamen kaldırıldı.
            End If
        Next

        UpdateQueueStats()

        ' Tüm liste eklendikten sonra kuyruk yöneticisini (paralel işlemciyi) ateşle
        Dim _ign = ProcessQueueAsync()
    End Sub

    Private Sub EnqueueUpload(items As IEnumerable(Of LocalFileItem))
        If Not _connected Then Return

        For Each item As LocalFileItem In items
            If item.Name = ".." Then Continue For

            Dim remote As String = _remoteDir.TrimEnd("/"c) & "/" & item.Name

            If item.IsDirectory Then
                ' Klasör yükleme: Arka planda klasörü tarayıp dosyaları kuyruğa ekler
                Dim _ign2 As System.Threading.Tasks.Task = Task.Run(Async Function()
                                                                        Await UploadDirAsync(item.FullPath, remote, CancellationToken.None)
                                                                        Await LoadRemote(_remoteDir)
                                                                        ' Klasör taraması bitince kuyruk işleyiciyi tekrar dürtüyoruz
                                                                        Await ProcessQueueAsync()
                                                                    End Function)
            Else
                ' Dosya yükleme: Doğrudan kuyruğa ekle
                Dim t As New TransferItem With {
                    .FileName = item.Name,
                    .LocalPath = item.FullPath,
                    .RemotePath = remote,
                    .FileSize = item.Size,
                    .Direction = TransferDirection.Upload,
                    .Site = _site,
                    .Status = TransferStatus.Queued ' ÖNEMLİ: Durumu "Queued" yapıyoruz
                }

                _transfers.Add(t)
                ' DÜZELTME: RunTransfer(t) satırı silindi. 
                ' Artık dosyaları ProcessQueueAsync metodu sırayla/paralel olarak yönetecek.
            End If
        Next

        UpdateQueueStats()

        ' Dosyalar listeye eklendi, şimdi paralel işlemciyi ateşliyoruz
        Dim _ign = ProcessQueueAsync()
    End Sub

    Private Async Function DownloadDirAsync(remotePath As String, localPath As String, ct As CancellationToken) As Task
        If Not Directory.Exists(localPath) Then Directory.CreateDirectory(localPath)
        Dim items As List(Of FtpFileItem) = Await _engine.ListDirectoryAsync(remotePath)
        For Each item As FtpFileItem In items
            ct.ThrowIfCancellationRequested()
            If item.IsDirectory Then
                Await DownloadDirAsync(remotePath.TrimEnd("/"c) & "/" & item.Name,
                    Path.Combine(localPath, item.Name), ct)
            Else
                Dim t As New TransferItem With {
                    .FileName = item.Name,
                    .LocalPath = Path.Combine(localPath, item.Name),
                    .RemotePath = remotePath.TrimEnd("/"c) & "/" & item.Name,
                    .FileSize = item.Size,
                    .Direction = TransferDirection.Download
                }
                Dispatcher.Invoke(Sub() _transfers.Add(t))
                Await _engine.DownloadFileAsync(t, ct)
            End If
        Next
    End Function

    Private Async Function UploadDirAsync(localPath As String, remotePath As String, ct As CancellationToken) As Task
        ' 1. Önce uzak sunucuda bu klasörü oluştur (Sırayla gitmesi dizin yapısı için iyidir)
        Try
            Await _engine.CreateDirectoryAsync(remotePath)
        Catch ex As Exception
            ' Klasör zaten varsa hata verebilir, sessizce devam edebiliriz
        End Try

        ' 2. Klasör içindeki dosyaları tara ve sadece KUYRUĞA EKLE
        For Each f As String In Directory.GetFiles(localPath)
            ct.ThrowIfCancellationRequested()
            Dim fi As New FileInfo(f)

            Dim t As New TransferItem With {
            .FileName = fi.Name,
            .LocalPath = f,
            .RemotePath = remotePath.TrimEnd("/"c) & "/" & fi.Name,
            .FileSize = fi.Length,
            .Direction = TransferDirection.Upload,
            .Status = TransferStatus.Queued, ' DURUM: Kuyrukta (Yönetici tarafından devralınacak)
            .Site = _site
        }

            ' Arayüz listesine ekle (Dispatcher ile güvenli ekleme)
            Dispatcher.Invoke(Sub() _transfers.Add(t))

            ' --- DÜZELTME: Await _engine.UploadFileAsync(t, ct) SATIRI SİLİNDİ ---
            ' Dosyayı burada bekleyerek yüklemiyoruz, listeye atıp devam ediyoruz.
        Next

        ' 3. Alt klasörler için kendini tekrar çağır (Rekürsif yapı)
        For Each d As String In Directory.GetDirectories(localPath)
            Await UploadDirAsync(d, remotePath.TrimEnd("/"c) & "/" & Path.GetFileName(d), ct)
        Next
    End Function

    Private Sub RunTransfer(t As TransferItem)
        Dim cts As New CancellationTokenSource()
        _cancelSources(t.Id) = cts
        Dim _ignRun As System.Threading.Tasks.Task = Task.Run(Async Function()
                                                                  Try
                                                                      If t.Direction = TransferDirection.Download Then
                                                                          Await _engine.DownloadFileAsync(t, cts.Token)
                                                                      Else
                                                                          Await _engine.UploadFileAsync(t, cts.Token)
                                                                      End If
                                                                  Finally
                                                                      _cancelSources.Remove(t.Id)
                                                                      Dispatcher.BeginInvoke(Sub()
                                                                                                 UpdateQueueStats()
                                                                                                 If t.Status = TransferStatus.Completed Then
                                                                                                     If t.Direction = TransferDirection.Download Then
                                                                                                         LoadLocal(_localDir)
                                                                                                     Else
                                                                                                         FireAndForget(LoadRemote(_remoteDir))
                                                                                                     End If
                                                                                                 End If
                                                                                             End Sub)
                                                                  End Try
                                                              End Function)
    End Sub

    ' ====== MENU EVENTS ======
    Private Sub mnuSiteManager_Click(sender As Object, e As RoutedEventArgs)
        OpenSiteManager()
    End Sub
    Private Sub mnuConnect_Click(sender As Object, e As RoutedEventArgs)
        OpenSiteManager()
    End Sub
    Private Sub mnuDisconnect_Click(sender As Object, e As RoutedEventArgs)
        If MessageBox.Show("Baglantıyı kes?", "Kes", MessageBoxButton.YesNo, MessageBoxImage.Question) = MessageBoxResult.Yes Then
            Disconnect()
        End If
    End Sub
    Private Sub mnuExit_Click(sender As Object, e As RoutedEventArgs)
        Close()
    End Sub
    Private Sub mnuUpload_Click(sender As Object, e As RoutedEventArgs)
        EnqueueUpload(GetLocalSelected())
    End Sub
    Private Sub mnuDownload_Click(sender As Object, e As RoutedEventArgs)
        EnqueueDownload(GetRemoteSelected())
    End Sub
    Private Sub mnuCancelAll_Click(sender As Object, e As RoutedEventArgs)
        For Each cts As CancellationTokenSource In _cancelSources.Values
            cts.Cancel()
        Next
    End Sub
    Private Sub mnuClearQueue_Click(sender As Object, e As RoutedEventArgs)
        For Each t As TransferItem In Enumerable.ToList(Enumerable.Where(_transfers, Function(x) Not x.IsActive))
            _transfers.Remove(t)
        Next
        UpdateQueueStats()
    End Sub
    Private Sub mnuRefresh_Click(sender As Object, e As RoutedEventArgs)
        If _connected Then FireAndForget(LoadRemote(_remoteDir))
    End Sub
    Private Sub mnuMkdir_Click(sender As Object, e As RoutedEventArgs)
        CreateRemoteDir()
    End Sub
    Private Sub mnuShowLog_Changed(sender As Object, e As RoutedEventArgs)
        ' Sayfa ilk yüklenirken alt nesneler henüz oluşmamış olabilir.
        ' Hepsini kontrol ediyoruz ki NullReference hatası almayalım.
        If rowLog IsNot Nothing AndAlso rowLogSplitter IsNot Nothing AndAlso
           gridLog IsNot Nothing AndAlso splitLog IsNot Nothing AndAlso
           mnuShowLog IsNot Nothing Then

            ' DÜZELTME: GetValueOrDefault() silindi, sadece IsChecked kontrol ediliyor
            If mnuShowLog.IsChecked Then
                ' Göster
                rowLog.Height = _logRowHeight
                rowLogSplitter.Height = New GridLength(5)
                gridLog.Visibility = Visibility.Visible
                splitLog.Visibility = Visibility.Visible
            Else
                ' Gizle
                _logRowHeight = rowLog.Height
                rowLog.Height = New GridLength(0)
                rowLogSplitter.Height = New GridLength(0)
                gridLog.Visibility = Visibility.Collapsed
                splitLog.Visibility = Visibility.Collapsed
            End If
        End If
    End Sub

    ' ====== TOOLBAR EVENTS ======
    Private Sub btnSiteManager_Click(sender As Object, e As RoutedEventArgs)
        OpenSiteManager()
    End Sub
    Private Sub btnConnect_Click(sender As Object, e As RoutedEventArgs)
        ' Sunucu alanı boşsa Site Yöneticisini aç
        If String.IsNullOrWhiteSpace(txtHost.Text) Then
            ' Varsa OpenSiteManager() metodunu çağır, yoksa aşağıdaki kodla Site Yöneticisini aç:
            Dim dlg As New SiteManagerWindow(_store)
            dlg.Owner = Me
            If dlg.ShowDialog() = True AndAlso dlg.SelectedSite IsNot Nothing Then
                ConnectTo(dlg.SelectedSite)
            End If
        Else
            ' Veriler doluysa doğrudan Hızlı Bağlantı metodunu çalıştır
            DoQuickConnect()
        End If
    End Sub
    Private Sub btnDisconnect_Click(sender As Object, e As RoutedEventArgs)
        Disconnect()
    End Sub
    Private Sub btnRefresh_Click(sender As Object, e As RoutedEventArgs)
        If _connected Then FireAndForget(LoadRemote(_remoteDir))
    End Sub
    Private Sub btnDownload_Click(sender As Object, e As RoutedEventArgs)
        EnqueueDownload(GetRemoteSelected())
    End Sub
    Private Sub btnUpload_Click(sender As Object, e As RoutedEventArgs)
        Dim sel As List(Of LocalFileItem) = Enumerable.ToList(Enumerable.Where(GetLocalSelected(), Function(i) i.Name <> ".."))
        If sel.Count > 0 Then EnqueueUpload(sel)
    End Sub
    Private Sub btnMkdir_Click(sender As Object, e As RoutedEventArgs)
        CreateRemoteDir()
    End Sub
    Private Sub btnDelete_Click(sender As Object, e As RoutedEventArgs)
        DeleteRemote()
    End Sub

    ' ====== QUICK CONNECT ======
    Private Sub btnQuickConnect_Click(sender As Object, e As RoutedEventArgs)
        ' Sunucu alanı boşsa Site Yöneticisini aç
        If String.IsNullOrWhiteSpace(txtHost.Text) Then
            ' Varsa OpenSiteManager() metodunu çağır, yoksa aşağıdaki kodla Site Yöneticisini aç:
            Dim dlg As New SiteManagerWindow(_store)
            dlg.Owner = Me
            If dlg.ShowDialog() = True AndAlso dlg.SelectedSite IsNot Nothing Then
                ConnectTo(dlg.SelectedSite)
            End If
        Else
            ' Veriler doluysa doğrudan Hızlı Bağlantı metodunu çalıştır
            DoQuickConnect()
        End If
    End Sub
    Private Sub QuickConnect_KeyDown(sender As Object, e As KeyEventArgs)
        If e.Key = Key.Enter Then DoQuickConnect()
    End Sub
    Private Sub QuickConnectPwd_KeyDown(sender As Object, e As KeyEventArgs)
        If e.Key = Key.Enter Then DoQuickConnect()
    End Sub
    Private Sub DoQuickConnect()
        If String.IsNullOrWhiteSpace(txtHost.Text) Then Return
        Dim port As Integer = 21
        Integer.TryParse(txtPort.Text, port)
        Dim hostVal As String = txtHost.Text.Trim()
        If hostVal.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase) Then
            hostVal = hostVal.Substring(6)
        ElseIf hostVal.StartsWith("ftps://", StringComparison.OrdinalIgnoreCase) Then
            hostVal = hostVal.Substring(7)
        End If
        hostVal = hostVal.TrimEnd("/"c)
        Dim s As New FtpSite With {
        .Name = hostVal,
        .Host = hostVal,
        .Port = port,
        .Username = If(String.IsNullOrWhiteSpace(txtUsername.Text), "anonymous", txtUsername.Text.Trim()),
        .Password = txtPassword.Password,
        .PassiveMode = True
    }
        ConnectTo(s)
    End Sub

    ' ====== LOCAL PANEL EVENTS ======
    Private Sub dgLocalFiles_MouseDoubleClick(sender As Object, e As System.Windows.Input.MouseButtonEventArgs)
        Dim item As LocalFileItem = TryCast(dgLocalFiles.SelectedItem, LocalFileItem)
        If item Is Nothing Then Return
        If item.IsDirectory Then LoadLocal(item.FullPath)
    End Sub
    Private Sub dgLocalFiles_KeyDown(sender As Object, e As KeyEventArgs)
        Select Case e.Key
            Case Key.Enter
                Dim item As LocalFileItem = TryCast(dgLocalFiles.SelectedItem, LocalFileItem)
                If item IsNot Nothing AndAlso item.IsDirectory Then LoadLocal(item.FullPath)
            Case Key.Delete
                DeleteLocal()
            Case Key.F2
                RenameLocal()
            Case Key.F8
                EnqueueUpload(GetLocalSelected())
        End Select
    End Sub
    Private Sub dgLocalFiles_PreviewKeyDown(sender As Object, e As KeyEventArgs)
        If e.Key = Key.Back Then
            Dim p As DirectoryInfo = Directory.GetParent(_localDir)
            If p IsNot Nothing Then LoadLocal(p.FullName)
            e.Handled = True
        End If
    End Sub
    Private Sub dgLocalFiles_ContextMenuOpening(sender As Object, e As ContextMenuEventArgs)
        Dim has As Boolean = dgLocalFiles.SelectedItems.Count > 0
        ctxLocalUpload.IsEnabled = _connected AndAlso has
        ctxLocalRename.IsEnabled = has AndAlso dgLocalFiles.SelectedItems.Count = 1
        ctxLocalDelete.IsEnabled = has
    End Sub
    Private Sub ctxLocalUpload_Click(sender As Object, e As RoutedEventArgs)
        EnqueueUpload(GetLocalSelected())
    End Sub
    Private Sub ctxLocalRename_Click(sender As Object, e As RoutedEventArgs)
        RenameLocal()
    End Sub
    Private Sub ctxLocalDelete_Click(sender As Object, e As RoutedEventArgs)
        DeleteLocal()
    End Sub
    Private Sub ctxLocalProps_Click(sender As Object, e As RoutedEventArgs)
        Dim item As LocalFileItem = TryCast(dgLocalFiles.SelectedItem, LocalFileItem)
        If item Is Nothing Then Return
        MessageBox.Show("Ad: " & item.Name & Environment.NewLine &
                        "Yol: " & item.FullPath & Environment.NewLine &
                        "Boyut: " & item.SizeDisplay & Environment.NewLine &
                        "Tarih: " & item.LastModifiedDisplay,
                        "Ozellikler", MessageBoxButton.OK, MessageBoxImage.Information)
    End Sub
    Private Sub btnLocalParent_Click(sender As Object, e As RoutedEventArgs)
        Dim p As DirectoryInfo = Directory.GetParent(_localDir)
        If p IsNot Nothing Then LoadLocal(p.FullName)
    End Sub
    Private Sub btnLocalRefresh_Click(sender As Object, e As RoutedEventArgs)
        LoadLocal(_localDir)
    End Sub
    Private Sub btnLocalMkdir_Click(sender As Object, e As RoutedEventArgs)
        Dim name As String = AskInput("Yeni klasor adı:", "Yeni Klasor", "yeni-klasor")
        If String.IsNullOrWhiteSpace(name) Then Return
        Try
            Directory.CreateDirectory(Path.Combine(_localDir, name))
            LoadLocal(_localDir)
        Catch ex As Exception
            MessageBox.Show("Hata: " & ex.Message, "Hata", MessageBoxButton.OK, MessageBoxImage.Error)
        End Try
    End Sub


    ' ====== REMOTE PANEL EVENTS ======
    Private Sub dgRemoteFiles_MouseDoubleClick(sender As Object, e As System.Windows.Input.MouseButtonEventArgs)
        Dim item As FtpFileItem = TryCast(dgRemoteFiles.SelectedItem, FtpFileItem)
        If item Is Nothing Then Return
        If item.IsDirectory Then
            Dim target As String = If(item.Name = "..", ParentPath(_remoteDir), item.FullPath)
            FireAndForget(LoadRemote(target))
        Else
            EnqueueDownload(New List(Of FtpFileItem) From {item})
        End If
    End Sub
    Private Sub dgRemoteFiles_KeyDown(sender As Object, e As KeyEventArgs)
        Select Case e.Key
            Case Key.Enter
                Dim item As FtpFileItem = TryCast(dgRemoteFiles.SelectedItem, FtpFileItem)
                If item IsNot Nothing AndAlso item.IsDirectory Then
                    Dim target As String = If(item.Name = "..", ParentPath(_remoteDir), item.FullPath)
                    FireAndForget(LoadRemote(target))
                End If
            Case Key.Delete
                DeleteRemote()
            Case Key.F2
                RenameRemote()
            Case Key.F7
                EnqueueDownload(GetRemoteSelected())
        End Select
    End Sub
    Private Sub dgRemoteFiles_PreviewKeyDown(sender As Object, e As KeyEventArgs)
        If e.Key = Key.Back AndAlso _connected AndAlso _remoteDir <> "/" Then
            FireAndForget(LoadRemote(ParentPath(_remoteDir)))
            e.Handled = True
        End If
    End Sub
    Private Sub dgRemoteFiles_DragOver(sender As Object, e As DragEventArgs)
        e.Effects = If(e.Data.GetDataPresent(DataFormats.FileDrop), DragDropEffects.Copy, DragDropEffects.None)
        e.Handled = True
    End Sub
    Private Async Sub dgRemoteFiles_Drop(sender As Object, e As DragEventArgs)
        If Not _connected Then Return

        If e.Data.GetDataPresent(DataFormats.FileDrop) Then
            Dim files As String() = DirectCast(e.Data.GetData(DataFormats.FileDrop), String())

            ' 1. Kullanıcıyı bilgilendir ve fare imlecini meşgul yap (Kullanıcı arayüzünün donmadığını anlar)
            If lblStatus IsNot Nothing Then lblStatus.Text = "Dosyalar taranıyor, lütfen bekleyin..."
            Mouse.OverrideCursor = Cursors.Wait ' WPF için bekleme imleci

            ' 2. Ağır dosya tarama işlemini arka plana (Task.Run) atıyoruz. UI bu sırada kilitlenmez.
            Dim items As List(Of LocalFileItem) = Await Task.Run(
            Function()
                Dim tempList As New List(Of LocalFileItem)()

                For Each f As String In files
                    ' Dosya mı klasör mü olduğunu doğrudan yoldan öğrenmek daha güvenlidir
                    Dim attr As FileAttributes = IO.File.GetAttributes(f)
                    Dim isDir As Boolean = (attr And FileAttributes.Directory) = FileAttributes.Directory

                    Dim fileSize As Long = 0
                    If Not isDir Then
                        ' Sadece dosyaysa boyut oku (Klasörlerde fi.Length hata fırlatabilir)
                        Dim fi As New FileInfo(f)
                        fileSize = If(fi.Exists, fi.Length, 0)
                    End If

                    tempList.Add(New LocalFileItem With {
                        .Name = IO.Path.GetFileName(f),
                        .FullPath = f,
                        .Size = fileSize,
                        .IsDirectory = isDir
                    })
                Next

                Return tempList
            End Function)

            ' 3. Await Task.Run bittikten sonra kod otomatik olarak ana UI Thread'ine döner.
            ' Bu sayede koleksiyona ekleme yapan EnqueueUpload metodunu güvenle çağırabiliriz.
            EnqueueUpload(items)

            ' 4. İşlem bitti, arayüzü normal haline geri döndür
            If lblStatus IsNot Nothing Then lblStatus.Text = "Hazır"
            Mouse.OverrideCursor = Nothing ' İmleci normale çevir
        End If
    End Sub
    Private Sub ctxDownload_Click(sender As Object, e As RoutedEventArgs)
        EnqueueDownload(GetRemoteSelected())
    End Sub
    Private Sub ctxRename_Click(sender As Object, e As RoutedEventArgs)
        RenameRemote()
    End Sub
    Private Sub ctxDelete_Click(sender As Object, e As RoutedEventArgs)
        DeleteRemote()
    End Sub
    Private Async Sub ctxRemoteProps_Click(sender As Object, e As RoutedEventArgs)
        If Not _connected OrElse _engine Is Nothing Then Return

        Dim item As FtpFileItem = TryCast(dgRemoteFiles.SelectedItem, FtpFileItem)
        If item Is Nothing OrElse item.Name = ".." Then Return

        Dim remotePath As String = _remoteDir.TrimEnd("/"c) & "/" & item.Name

        ' =========================================================================
        ' DÜZELTME BURADA: Dosyanın GERÇEK iznini okuyup rakama çeviriyoruz!
        ' =========================================================================
        Dim currentNumeric As String = ConvertPermsToNumeric(item.Permissions)
        Dim dlg As New ChmodWindow(item.Name, currentNumeric)
        dlg.Owner = Me

        If dlg.ShowDialog() = True Then
            Dim newPerms As String = dlg.NumericPermissions

            Try
                SetStatus("İzinler güncelleniyor: " & newPerms)

                Await _engine.SetPermissionsAsync(remotePath, newPerms)
                Log(item.Name & " dosyasının izinleri " & newPerms & " olarak değiştirildi.", LogLevel.Success)

                Await Task.Delay(500)
                Await LoadRemote(_remoteDir)

            Catch ex As Exception
                Log("İzin değiştirme hatası: " & ex.Message, LogLevel.Error)
                MessageBox.Show("CHMOD işlemi başarısız oldu." & Environment.NewLine & "Hata: " & ex.Message, "Yetki Hatası", MessageBoxButton.OK, MessageBoxImage.Warning)
            End Try
        End If
    End Sub
    Private Sub btnRemoteParent_Click(sender As Object, e As RoutedEventArgs)
        If Not _connected OrElse _remoteDir = "/" Then Return
        FireAndForget(LoadRemote(ParentPath(_remoteDir)))
    End Sub
    Private Sub btnRemoteRefresh_Click(sender As Object, e As RoutedEventArgs)
        If _connected Then FireAndForget(LoadRemote(_remoteDir))
    End Sub
    Private Sub btnRemoteMkdir_Click(sender As Object, e As RoutedEventArgs)
        CreateRemoteDir()
    End Sub
    Private Sub txtRemotePath_KeyDown(sender As Object, e As KeyEventArgs)
        If e.Key = Key.Enter Then
            Dim p As String = txtRemotePath.Text.Trim()
            If Not p.StartsWith("/") Then p = "/" & p
            FireAndForget(LoadRemote(p))
        End If
    End Sub
    Private Sub btnRemoteGo_Click(sender As Object, e As RoutedEventArgs)
        Dim p As String = txtRemotePath.Text.Trim()
        If Not p.StartsWith("/") Then p = "/" & p
        FireAndForget(LoadRemote(p))
    End Sub

    ' ====== FILE OPERATIONS ======
    Private Async Sub DeleteRemote()
        If Not _connected Then Return
        Dim items As List(Of FtpFileItem) = Enumerable.ToList(Enumerable.Where(GetRemoteSelected(), Function(i) i.Name <> ".."))
        If items.Count = 0 Then Return
        Dim msg As String = If(items.Count = 1, "'" & items(0).Name & "' silinsin mi?",
                                items.Count.ToString() & " oge silinsin mi?")
        If MessageBox.Show(msg, "Sil", MessageBoxButton.YesNo, MessageBoxImage.Warning) <> MessageBoxResult.Yes Then Return
        For Each item As FtpFileItem In items
            If item.IsDirectory Then
                Await _engine.DeleteDirectoryAsync(item.FullPath)
            Else
                Await _engine.DeleteFileAsync(item.FullPath)
            End If
        Next
        Await LoadRemote(_remoteDir)
    End Sub

    Private Async Sub RenameRemote()
        If Not _connected Then Return
        Dim item As FtpFileItem = TryCast(dgRemoteFiles.SelectedItem, FtpFileItem)
        If item Is Nothing OrElse item.Name = ".." Then Return
        Dim newName As String = AskInput("Yeni ad:", "Yeniden Adlandır", item.Name)
        If String.IsNullOrWhiteSpace(newName) OrElse newName = item.Name Then Return
        Dim newPath As String = _remoteDir.TrimEnd("/"c) & "/" & newName
        Await _engine.RenameAsync(item.FullPath, newPath)
        Await LoadRemote(_remoteDir)
    End Sub

    Private Async Sub CreateRemoteDir()
        If Not _connected Then Return
        Dim name As String = AskInput("Yeni klasor adı:", "Yeni Klasor", "yeni-klasor")
        If String.IsNullOrWhiteSpace(name) Then Return
        Await _engine.CreateDirectoryAsync(_remoteDir.TrimEnd("/"c) & "/" & name)
        Await LoadRemote(_remoteDir)
    End Sub

    Private Sub DeleteLocal()
        Dim items As List(Of LocalFileItem) = Enumerable.ToList(Enumerable.Where(GetLocalSelected(), Function(i) i.Name <> ".."))
        If items.Count = 0 Then Return
        If MessageBox.Show(items.Count.ToString() & " oge silinsin mi?", "Sil",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) <> MessageBoxResult.Yes Then Return
        For Each item As LocalFileItem In items
            Try
                If item.IsDirectory Then Directory.Delete(item.FullPath, True) Else File.Delete(item.FullPath)
            Catch ex As Exception
                Log("Silme hatası: " & ex.Message, LogLevel.Error)
            End Try
        Next
        LoadLocal(_localDir)
    End Sub

    Private Sub RenameLocal()
        Dim item As LocalFileItem = TryCast(dgLocalFiles.SelectedItem, LocalFileItem)
        If item Is Nothing OrElse item.Name = ".." Then Return
        Dim newName As String = AskInput("Yeni ad:", "Yeniden Adlandır", item.Name)
        If String.IsNullOrWhiteSpace(newName) OrElse newName = item.Name Then Return
        Try
            Dim newPath As String = Path.Combine(_localDir, newName)
            If item.IsDirectory Then Directory.Move(item.FullPath, newPath) Else File.Move(item.FullPath, newPath)
            LoadLocal(_localDir)
        Catch ex As Exception
            MessageBox.Show("Hata: " & ex.Message, "Hata", MessageBoxButton.OK, MessageBoxImage.Error)
        End Try
    End Sub

    ' ====== TRANSFER QUEUE EVENTS ======

    Private Sub btnCancelAll_Click(sender As Object, e As RoutedEventArgs)
        For Each cts As CancellationTokenSource In _cancelSources.Values
            cts.Cancel()
        Next
        For Each t As TransferItem In Enumerable.ToList(Enumerable.Where(_transfers, Function(x) x.Status = TransferStatus.Queued))
            t.Status = TransferStatus.Cancelled
        Next
    End Sub
    Private Sub btnClearCompleted_Click(sender As Object, e As RoutedEventArgs)
        For Each t As TransferItem In Enumerable.ToList(Enumerable.Where(_transfers, Function(x) Not x.IsActive))
            _transfers.Remove(t)
        Next
        UpdateQueueStats()
    End Sub
    Private Sub btnClearLog_Click(sender As Object, e As RoutedEventArgs)
        _logs.Clear()
    End Sub
    ' ====== SITE MANAGER ======
    Private Sub OpenSiteManager()
        Dim dlg As New SiteManagerWindow(_store)
        dlg.Owner = Me

        If dlg.ShowDialog() = True AndAlso dlg.SelectedSite IsNot Nothing Then

            ' ---------------------------------------------------------------------
            ' AKILLI BAĞLANTI KONTROLÜ
            ' ---------------------------------------------------------------------
            If _connected Then
                ' vbCrLf YERİNE Environment.NewLine KULLANIYORUZ
                Dim cevap As MessageBoxResult = MessageBox.Show(
                    "Şu anda aktif bir sunucuya bağlısınız." & Environment.NewLine & Environment.NewLine &
                    "Mevcut bağlantı kapatılıp yeni sunucuya bağlanılsın mı?",
                    "Aktif Bağlantı Uyarısı",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question)

                If cevap = MessageBoxResult.No Then
                    Return
                Else
                    Disconnect() ' Sende bağlantıyı kesen metodun adı (Örn: Disconnect veya btnDisconnect_Click)
                End If
            End If
            ' ---------------------------------------------------------------------

            ConnectTo(dlg.SelectedSite)

        End If
    End Sub

    ' ====== KEYBOARD SHORTCUTS ======
    Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
        If e.Key = Key.F5 Then
            If _connected Then FireAndForget(LoadRemote(_remoteDir))
            e.Handled = True
        ElseIf e.Key = Key.S AndAlso Keyboard.Modifiers = ModifierKeys.Control Then
            OpenSiteManager()
            e.Handled = True
        End If
        MyBase.OnKeyDown(e)
    End Sub

    Protected Overrides Sub OnClosing(e As System.ComponentModel.CancelEventArgs)
        Dim active As Integer = Enumerable.Count(_transfers, Function(t) t.IsActive)
        If active > 0 Then
            If MessageBox.Show(active.ToString() & " aktif transfer var. Cikis yapilsin mi?",
                "Cikis", MessageBoxButton.YesNo, MessageBoxImage.Warning) = MessageBoxResult.No Then
                e.Cancel = True
                Return
            End If
        End If
        For Each cts As CancellationTokenSource In _cancelSources.Values
            cts.Cancel()
        Next
        _engine?.Dispose()
        MyBase.OnClosing(e)
    End Sub

    Private Sub FireAndForget(t As System.Threading.Tasks.Task)
        ' fire-and-forget - gorevi baslar, beklemez
    End Sub


    Private Sub btnTheme_Click(sender As Object, e As RoutedEventArgs)
        ' Tema modunu her tıklamada tam tersine çeviriyoruz
        _isDarkMode = Not _isDarkMode

        ' Ve temayı uyguluyoruz
        ApplyTheme()
    End Sub

    Private Sub ApplyTheme()
        Dim res As ResourceDictionary = Application.Current.Resources

        If _isDarkMode Then
            ' KOYU MOD (Eski ayarların kalabilir)
            res("BgDeepBrush") = New SolidColorBrush(DirectCast(ColorConverter.ConvertFromString("#0F1319"), Color))
            res("BgPrimaryBrush") = New SolidColorBrush(DirectCast(ColorConverter.ConvertFromString("#161B24"), Color))
            res("BgSecondaryBrush") = New SolidColorBrush(DirectCast(ColorConverter.ConvertFromString("#1E2535"), Color))
            res("BgTertiaryBrush") = New SolidColorBrush(DirectCast(ColorConverter.ConvertFromString("#252E3F"), Color))
            res("BgHoverBrush") = New SolidColorBrush(DirectCast(ColorConverter.ConvertFromString("#2D3A50"), Color))
            res("TextPrimaryBrush") = New SolidColorBrush(DirectCast(ColorConverter.ConvertFromString("#E8EDF5"), Color))
            res("TextSecBrush") = New SolidColorBrush(DirectCast(ColorConverter.ConvertFromString("#8A9BB5"), Color))
            res("TextMutedBrush") = New SolidColorBrush(DirectCast(ColorConverter.ConvertFromString("#4A5568"), Color))
            res("BorderBrush") = New SolidColorBrush(DirectCast(ColorConverter.ConvertFromString("#2A3448"), Color))
            res("SelectBrush") = New SolidColorBrush(DirectCast(ColorConverter.ConvertFromString("#1A3F6F"), Color))
            res("AccentDarkBrush") = New SolidColorBrush(DirectCast(ColorConverter.ConvertFromString("#7CC5FF"), Color)) ' Koyu mod klasör rengi (Açık Mavi)

            ' XAML'daki menü renkleri (Manuel atadıkların)
            res("Menu.Static.Background") = res("BgDeepBrush")
            res("Menu.Static.Foreground") = res("TextPrimaryBrush")
            res("MenuItem.Selected.Background") = res("SelectBrush")

            ' İNATÇI BEYAZ ŞERİDİ VE COMBOBOX BEYAZLIĞINI YOK EDEN SİHİRLİ SATIRLAR
            res(SystemColors.MenuBrushKey) = res("BgSecondaryBrush")
            res(SystemColors.MenuBarBrushKey) = res("BgDeepBrush")
            res(SystemColors.WindowBrushKey) = res("BgDeepBrush")
            res(SystemColors.ControlBrushKey) = res("BgSecondaryBrush") ' ComboBox arka planı için ek güvenlik

            lblTheme.Text = "☀ Acik Mod"
        Else
            ' AÇIK MOD (Yenilenmiş, modern ve okunabilir palet)
            res("BgDeepBrush") = New SolidColorBrush(DirectCast(ColorConverter.ConvertFromString("#F3F4F6"), Color)) ' Hafif gri arka plan
            res("BgPrimaryBrush") = New SolidColorBrush(DirectCast(ColorConverter.ConvertFromString("#FFFFFF"), Color)) ' Beyaz listeler
            res("BgSecondaryBrush") = New SolidColorBrush(DirectCast(ColorConverter.ConvertFromString("#F9FAFB"), Color)) ' Çok açık gri paneller
            res("BgTertiaryBrush") = New SolidColorBrush(DirectCast(ColorConverter.ConvertFromString("#E5E7EB"), Color)) ' Hover ön izleme
            res("BgHoverBrush") = New SolidColorBrush(DirectCast(ColorConverter.ConvertFromString("#E5E7EB"), Color)) ' Fare ile üzerine gelme
            res("TextPrimaryBrush") = New SolidColorBrush(DirectCast(ColorConverter.ConvertFromString("#111827"), Color)) ' Koyu gri/siyah metin (Dosyalar için)
            res("TextSecBrush") = New SolidColorBrush(DirectCast(ColorConverter.ConvertFromString("#4B5563"), Color)) ' Orta gri alt metinler
            res("TextMutedBrush") = New SolidColorBrush(DirectCast(ColorConverter.ConvertFromString("#9CA3AF"), Color)) ' Soluk detaylar
            res("BorderBrush") = New SolidColorBrush(DirectCast(ColorConverter.ConvertFromString("#D1D5DB"), Color)) ' Narin sınırlar
            res("SelectBrush") = New SolidColorBrush(DirectCast(ColorConverter.ConvertFromString("#DBEAFE"), Color)) ' Çok tatlı açık mavi seçim
            res("AccentDarkBrush") = New SolidColorBrush(DirectCast(ColorConverter.ConvertFromString("#1E3A8A"), Color)) ' Klasörler için Koyu Lacivert!

            ' XAML'daki menü renkleri (Manuel atadıkların)
            res("Menu.Static.Background") = New SolidColorBrush(Colors.White)
            res("Menu.Static.Foreground") = New SolidColorBrush(Colors.Black)
            res("MenuItem.Selected.Background") = New SolidColorBrush(ColorConverter.ConvertFromString("#DBEAFE"))

            ' Sistem renklerini normale döndür (Açık Mod İçin)
            res(SystemColors.MenuBrushKey) = New SolidColorBrush(Colors.White)
            res(SystemColors.MenuBarBrushKey) = New SolidColorBrush(DirectCast(ColorConverter.ConvertFromString("#F3F4F6"), Color))
            res(SystemColors.WindowBrushKey) = New SolidColorBrush(Colors.White)
            res(SystemColors.ControlBrushKey) = New SolidColorBrush(DirectCast(ColorConverter.ConvertFromString("#F9FAFB"), Color))

            lblTheme.Text = "🌙 Koyu Mod"
        End If
    End Sub
    Private Sub txtHost_TextChanged(sender As Object, e As TextChangedEventArgs)
        Dim text As String = txtHost.Text.ToLower()
        If text.StartsWith("ftp://") Then
            txtHost.Text = txtHost.Text.Substring(6)
            txtHost.CaretIndex = txtHost.Text.Length ' İmleci yazının sonuna al
        ElseIf text.StartsWith("ftps://") Then
            txtHost.Text = txtHost.Text.Substring(7)
            txtHost.CaretIndex = txtHost.Text.Length
        End If
    End Sub
    ' Harfli UNIX izinlerini (örn: -rwxr-xr-x) rakamsal değere (örn: 755) çeviren akıllı metot
    ' Harfli UNIX izinlerini (örn: -rwxr-xr-x) rakamsal değere (örn: 755) çeviren akıllı metot
    Private Function ConvertPermsToNumeric(perms As String) As String
        ' İzin bilgisi boşsa veya eksikse standart dosyalar için 644 döndür
        If String.IsNullOrWhiteSpace(perms) OrElse perms.Length < 10 Then
            Return "644"
        End If

        Try
            Dim valOwner As Integer = 0
            Dim valGroup As Integer = 0
            Dim valPublic As Integer = 0

            ' 1. Sahip (Owner) İzinleri: 1, 2, 3. karakterler
            If perms(1) = "r"c Then valOwner += 4
            If perms(2) = "w"c Then valOwner += 2
            If perms(3) = "x"c OrElse perms(3) = "s"c OrElse perms(3) = "S"c Then valOwner += 1

            ' 2. Grup (Group) İzinleri: 4, 5, 6. karakterler
            If perms(4) = "r"c Then valGroup += 4
            If perms(5) = "w"c Then valGroup += 2
            If perms(6) = "x"c OrElse perms(6) = "s"c OrElse perms(6) = "S"c Then valGroup += 1

            ' 3. Genel (Public) İzinleri: 7, 8, 9. karakterler
            If perms(7) = "r"c Then valPublic += 4
            If perms(8) = "w"c Then valPublic += 2
            If perms(9) = "x"c OrElse perms(9) = "t"c OrElse perms(9) = "T"c Then valPublic += 1

            Return valOwner.ToString() & valGroup.ToString() & valPublic.ToString()
        Catch
            Return "644" ' Herhangi bir hatada çökmemesi için güvenlik ağı
        End Try
    End Function
    ' ==========================================
    ' YEREL SİSTEM: DOSYA DÜZENLEME (AÇMA)
    ' ==========================================
    Private Sub ctxLocalEdit_Click(sender As Object, e As RoutedEventArgs)
        ' XAML'daki LocalFileItem sınıfının adını kendi projende nasıl tanımladıysan ona göre düzeltmelisin 
        ' (Örn: System.IO.FileInfo veya senin yazdığın LocalFileItem sınıfı)
        Dim item = TryCast(dgLocalFiles.SelectedItem, LocalFileItem)

        ' Hiçbir şey seçilmediyse veya klasör seçildiyse işlem yapma
        If item Is Nothing OrElse item.IsDirectory OrElse item.Name = ".." Then Return

        Try
            ' Dosyayı sistemin varsayılan uygulamasıyla (Örn: Notepad) aç
            Process.Start(New ProcessStartInfo(item.FullPath) With {.UseShellExecute = True})
        Catch ex As Exception
            MessageBox.Show("Dosya açılamadı: " & ex.Message, "Hata", MessageBoxButton.OK, MessageBoxImage.Error)
        End Try
    End Sub
    ' ==========================================
    ' UZAK SİSTEM: PROFESYONEL CANLI DÜZENLEME (LIVE EDIT)
    ' ==========================================
    Private Async Sub ctxRemoteEdit_Click(sender As Object, e As RoutedEventArgs)
        If Not _connected OrElse _engine Is Nothing Then Return
        Dim item As FtpFileItem = TryCast(dgRemoteFiles.SelectedItem, FtpFileItem)

        ' Klasörler düzenlenemez, sadece dosyalar!
        If item Is Nothing OrElse item.IsDirectory OrElse item.Name = ".." Then Return

        Dim remotePath As String = _remoteDir.TrimEnd("/"c) & "/" & item.Name

        ' 1. Güvenli bir Geçici (Temp) klasörü oluşturuyoruz
        Dim tempDir As String = IO.Path.Combine(IO.Path.GetTempPath(), "FTPClientPro_Edit")
        If Not IO.Directory.Exists(tempDir) Then IO.Directory.CreateDirectory(tempDir)

        Dim localTempPath As String = IO.Path.Combine(tempDir, item.Name)

        Try
            SetStatus("Düzenleme için indiriliyor...")

            ' 2. Dosyayı sessizce Temp klasörüne indir
            Dim t As New TransferItem With {
                .FileName = item.Name,
                .LocalPath = localTempPath,
                .RemotePath = remotePath,
                .Direction = TransferDirection.Download
            }

            Dim success As Boolean = Await _engine.DownloadFileAsync(t, Threading.CancellationToken.None)
            If Not success Then
                MessageBox.Show("Dosya indirilemedi, düzenleme başlatılamıyor.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error)
                Return
            End If

            Log(item.Name & " düzenleme için arka planda açıldı.", LogLevel.Info)

            ' 3. Dosyayı Sistemdeki Varsayılan Editörle (Notepad vb.) Aç!
            Process.Start(New ProcessStartInfo(localTempPath) With {.UseShellExecute = True})

            ' 4. Zaten bu dosya için çalışan bir gözlemci varsa kapatıp sıfırlayalım
            If _fileWatchers.ContainsKey(localTempPath) Then
                _fileWatchers(localTempPath).Dispose()
                _fileWatchers.Remove(localTempPath)
            End If

            ' 5. PROFESYONEL DOSYA GÖZLEMCİSİ (Hafiye) Başlat
            Dim watcher As New IO.FileSystemWatcher(tempDir, item.Name)
            watcher.NotifyFilter = IO.NotifyFilters.LastWrite

            AddHandler watcher.Changed,
                Sub(s, ev)
                    ' Editörler (VS Code vb.) CTRL+S yapınca dosyaya saniyede 2-3 kez yazar.
                    ' Bu spam'i engellemek için 2 saniyelik bir bariyer kuruyoruz:
                    If (DateTime.Now - _lastUploadTime).TotalSeconds < 2 Then Return
                    _lastUploadTime = DateTime.Now

                    ' Olay arka planda tetiklendiği için Ana Ekran (UI) thread'ine dönüyoruz
                    Dispatcher.BeginInvoke(
                        Async Sub()
                            Try
                                Log(item.Name & " üzerinde değişiklik algılandı, sunucuya yükleniyor...", LogLevel.Info)
                                SetStatus("Canlı Düzenleme: Yükleniyor...")

                                Dim uploadTask As New TransferItem With {
                                    .FileName = item.Name,
                                    .LocalPath = localTempPath,
                                    .RemotePath = remotePath,
                                    .Direction = TransferDirection.Upload
                                }

                                Dim upSuccess As Boolean = Await _engine.UploadFileAsync(uploadTask, Threading.CancellationToken.None)
                                If upSuccess Then
                                    Log(item.Name & " (Live Edit) başarıyla FTP'ye güncellendi.", LogLevel.Success)
                                    Await LoadRemote(_remoteDir) ' Listeyi yenile ki tarih/boyut güncellensin
                                Else
                                    Log(item.Name & " yüklenirken hata oluştu.", LogLevel.Error)
                                End If
                                SetStatus("Hazır")
                            Catch exUpload As Exception
                                Log("Canlı yükleme hatası: " & exUpload.Message, LogLevel.Error)
                            End Try
                        End Sub)
                End Sub

            ' Gözlemciyi Aktif Et!
            watcher.EnableRaisingEvents = True
            _fileWatchers.Add(localTempPath, watcher)

        Catch ex As Exception
            Log("Düzenleme başlatılamadı: " & ex.Message, LogLevel.Error)
        End Try
    End Sub
    ' ==========================================
    ' GÖRÜNÜM: TRANSFER KUYRUĞUNU GİZLE / GÖSTER
    ' ==========================================
    Private Sub mnuShowTransfer_Changed(sender As Object, e As RoutedEventArgs)
        ' Sayfa ilk yüklenirken alt nesneler henüz oluşmamış olabilir.
        ' NullReference hatası almamak için hepsini kontrol ediyoruz.
        If rowTransfer IsNot Nothing AndAlso rowTransferSplitter IsNot Nothing AndAlso
           gridTransfer IsNot Nothing AndAlso splitTransfer IsNot Nothing AndAlso
           mnuShowTransfer IsNot Nothing Then

            If mnuShowTransfer.IsChecked Then
                ' GÖSTER: Satırı eski yüksekliğine döndür ve görünür yap
                rowTransfer.Height = _transferRowHeight
                rowTransferSplitter.Height = New GridLength(5)
                gridTransfer.Visibility = Visibility.Visible
                splitTransfer.Visibility = Visibility.Visible
            Else
                ' GİZLE: Mevcut yüksekliği hafızaya al, satırı tamamen daralt (0 piksel yap)
                _transferRowHeight = rowTransfer.Height
                rowTransfer.Height = New GridLength(0)
                rowTransferSplitter.Height = New GridLength(0)
                gridTransfer.Visibility = Visibility.Collapsed
                splitTransfer.Visibility = Visibility.Collapsed
            End If
        End If
    End Sub
    ' ==========================================
    ' HIZLI FİLTRELEME (ARAMA KUTULARI)
    ' ==========================================
    Private Sub txtLocalSearch_TextChanged(sender As Object, e As TextChangedEventArgs)
        If dgLocalFiles.ItemsSource Is Nothing Then Return
        Dim cv As System.ComponentModel.ICollectionView = CollectionViewSource.GetDefaultView(dgLocalFiles.ItemsSource)
        If cv IsNot Nothing Then
            Dim keyword As String = txtLocalSearch.Text.ToLower()
            cv.Filter = Function(item)
                            Dim fItem = TryCast(item, Object) ' LocalItem sınıfın neyse ona göre çalışır
                            Dim nameProp = fItem.GetType().GetProperty("Name")
                            If nameProp Is Nothing Then Return True
                            Dim nameVal As String = nameProp.GetValue(fItem, Nothing).ToString()

                            If nameVal = ".." Then Return True ' Üst klasör hep görünsün
                            Return nameVal.ToLower().Contains(keyword)
                        End Function
        End If
    End Sub

    Private Sub txtRemoteSearch_TextChanged(sender As Object, e As TextChangedEventArgs)
        If dgRemoteFiles.ItemsSource Is Nothing Then Return
        Dim cv As System.ComponentModel.ICollectionView = CollectionViewSource.GetDefaultView(dgRemoteFiles.ItemsSource)
        If cv IsNot Nothing Then
            Dim keyword As String = txtRemoteSearch.Text.ToLower()
            cv.Filter = Function(item)
                            Dim fItem = TryCast(item, FtpFileItem)
                            If fItem Is Nothing Then Return False
                            If fItem.Name = ".." Then Return True ' Üst klasör hep görünsün
                            Return fItem.Name.ToLower().Contains(keyword)
                        End Function
        End If
    End Sub
    Private Sub cmbSpeedLimit_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)
        If _engine Is Nothing Then Return

        ' ComboBoxIndex'e göre KB cinsinden limitler
        Select Case cmbSpeedLimit.SelectedIndex
            Case 0 : _engine.SpeedLimitKbps = 0    ' Sınırsız
            Case 1 : _engine.SpeedLimitKbps = 100  ' 100 KB/s
            Case 2 : _engine.SpeedLimitKbps = 500  ' 500 KB/s
            Case 3 : _engine.SpeedLimitKbps = 1024 ' 1 MB/s
            Case 4 : _engine.SpeedLimitKbps = 5120 ' 5 MB/s
        End Select

        Log("Hız sınırı güncellendi: " & cmbSpeedLimit.SelectedItem.ToString(), LogLevel.Info)
    End Sub
    ' ==========================================
    ' KLASÖR KARŞILAŞTIRMA MANTIĞI
    ' ==========================================
    Private Sub btnCompare_Click(sender As Object, e As RoutedEventArgs)
        ' Listeleri alıyoruz
        Dim localItems = dgLocalFiles.ItemsSource?.Cast(Of LocalFileItem)().ToList()
        Dim remoteItems = dgRemoteFiles.ItemsSource?.Cast(Of FtpFileItem)().ToList()

        If localItems Is Nothing OrElse remoteItems Is Nothing Then
            Log("Karşılaştırma için her iki listenin de dolu olması gerekir.", LogLevel.Warning)
            Return
        End If

        ' 1. Önce tüm durumları sıfırlayalım (Eski aramadan kalan renkler gitsin)
        For Each li In localItems : li.ComparisonStatus = "Normal" : Next
        For Each ri In remoteItems : ri.ComparisonStatus = "Normal" : Next

        ' 2. Yerel dosyaları Uzak dosyalarla kıyasla
        For Each li In localItems
            If li.Name = ".." Then Continue For

            ' Aynı isimdeki uzak dosyayı bul
            Dim match = remoteItems.FirstOrDefault(Function(x) x.Name = li.Name)

            If match Is Nothing Then
                li.ComparisonStatus = "Missing" ' Uzakta yok
            Else
                ' İsimler aynı, boyutları kontrol et
                If li.Size <> match.Size Then
                    li.ComparisonStatus = "Different"
                    match.ComparisonStatus = "Different"
                End If
            End If
        Next

        ' 3. Uzak dosyaları Yerel dosyalarla kıyasla (Yerelde eksik olanları bulmak için)
        For Each ri In remoteItems
            If ri.Name = ".." Then Continue For
            Dim match = localItems.FirstOrDefault(Function(x) x.Name = ri.Name)
            If match Is Nothing Then
                ri.ComparisonStatus = "Missing" ' Yerelde yok
            End If
        Next

        ' 4. Listeleri tazeleyelim (Renklerin ekrana yansıması için şart!)
        dgLocalFiles.Items.Refresh()
        dgRemoteFiles.Items.Refresh()

        Log("Karşılaştırma tamamlandı: Kırmızı = Eksik, Sarı = Farklı Boyut", LogLevel.Info)
    End Sub

    Private Function EncryptPassword(clearText As String) As String
        If String.IsNullOrEmpty(clearText) Then Return ""
        Try
            Dim clearBytes = Encoding.UTF8.GetBytes(clearText)
            Dim encryptedBytes = ProtectedData.Protect(clearBytes, Nothing, DataProtectionScope.CurrentUser)
            Return Convert.ToBase64String(encryptedBytes)
        Catch
            Return ""
        End Try
    End Function

    Private Function DecryptPassword(encryptedText As String) As String
        If String.IsNullOrEmpty(encryptedText) Then Return ""
        Try
            Dim encryptedBytes = Convert.FromBase64String(encryptedText)
            Dim clearBytes = ProtectedData.Unprotect(encryptedBytes, Nothing, DataProtectionScope.CurrentUser)
            Return Encoding.UTF8.GetString(clearBytes)
        Catch
            Return ""
        End Try
    End Function
    ' Başarılı bağlantıyı geçmişe ekle
    Private Sub AddToHistory(site As FtpSite)
        _history.RemoveAll(Function(x) x.Host.ToLower() = site.Host.ToLower() AndAlso x.Username.ToLower() = site.Username.ToLower())
        _history.Insert(0, site)
        If _history.Count > 10 Then _history = _history.Take(10).ToList()
        Try
            ' Şifreyi DPAPI ile şifreleyerek kaydediyoruz
            Dim lines = _history.Select(Function(x) $"{x.Host}|{x.Username}|{x.Port}|{EncryptPassword(x.Password)}")
            File.WriteAllLines(HistoryFile, lines)
        Catch : End Try
    End Sub

    Private Sub LoadHistory()
        Try
            If File.Exists(HistoryFile) Then
                Dim lines = File.ReadAllLines(HistoryFile)
                _history.Clear()
                For Each line In lines
                    Dim parts = line.Split("|"c)
                    If parts.Length >= 3 Then
                        Dim pass As String = If(parts.Length >= 4, DecryptPassword(parts(3)), "")
                        _history.Add(New FtpSite With {
                            .Host = parts(0),
                            .Username = parts(1),
                            .Port = CInt(parts(2)),
                            .Name = parts(0),
                            .Password = pass
                        })
                    End If
                Next
            End If
        Catch : End Try
    End Sub
    Private Sub btnHistory_Click(sender As Object, e As RoutedEventArgs)
        cmHistory.Items.Clear()

        If _history.Count = 0 Then
            cmHistory.Items.Add(New MenuItem With {.Header = "Henüz geçmiş yok", .IsEnabled = False})
        Else
            For Each site In _history
                Dim mi As New MenuItem With {
                    .Header = $"{site.Username}@{site.Host}:{site.Port}",
                    .Icon = "🌐",
                    .Tag = site
                }
                AddHandler mi.Click, AddressOf HistoryItem_Click
                cmHistory.Items.Add(mi)
            Next
            cmHistory.Items.Add(New Separator())
            Dim clearMi As New MenuItem With {.Header = "Geçmişi Temizle"}
            AddHandler clearMi.Click, Sub()
                                          _history.Clear()
                                          If File.Exists(HistoryFile) Then File.Delete(HistoryFile)
                                      End Sub
            cmHistory.Items.Add(clearMi)
        End If

        cmHistory.PlacementTarget = btnHistory
        cmHistory.IsOpen = True
    End Sub

    Private Sub HistoryItem_Click(sender As Object, e As RoutedEventArgs)
        Dim site = TryCast(DirectCast(sender, MenuItem).Tag, FtpSite)
        If site IsNot Nothing Then
            txtHost.Text = site.Host
            txtUsername.Text = site.Username
            txtPort.Text = site.Port.ToString()
            txtPassword.Focus() ' Şifreyi güvenlik için kaydetmiyoruz, kullanıcı yazsın
            Log("Geçmişten bilgiler dolduruldu. Lütfen şifrenizi girin.", LogLevel.Info)
        End If
    End Sub
    ' Menüdeki "Hakkında" butonuna tıklandığında
    Private Sub mnuAbout_Click(sender As Object, e As RoutedEventArgs)
        Dim frm As New AboutControl()
        frm.Owner = Me ' Ana pencerenin üzerinde kalsın
        frm.ShowDialog() ' Formu aç
    End Sub
    ' ===========================================================================
    ' YENİ NESİL TRANSFER METOTLARI (TÜRKÇE KARAKTER UYUMLU & ÇÖKME KORUMALI)
    ' ===========================================================================
    Private Async Function UploadFileWithResumeAsync(t As TransferItem, ct As CancellationToken) As Task(Of Boolean)
        Dim request As FtpWebRequest = Nothing
        Dim reqStream As IO.Stream = Nothing
        Dim locStream As IO.FileStream = Nothing

        Try
            t.Status = TransferStatus.Transferring

            Dim remoteUrl As String = "ftp://" & t.Site.Host & ":" & t.Site.Port.ToString() & t.RemotePath

            Dim remoteFileSize As Long = GetRemoteFileSize(remoteUrl, t.Site.Username, t.Site.Password, ct)
            Dim localFileInfo As New IO.FileInfo(t.LocalPath)

            If remoteFileSize >= localFileInfo.Length Then
                t.Progress = 100
                t.Status = TransferStatus.Completed
                Return True
            End If

            request = CType(WebRequest.Create(New Uri(remoteUrl)), FtpWebRequest)
            request.Credentials = New NetworkCredential(t.Site.Username, t.Site.Password)
            request.Method = If(remoteFileSize > 0, WebRequestMethods.Ftp.AppendFile, WebRequestMethods.Ftp.UploadFile)
            request.UseBinary = True
            request.KeepAlive = False

            Using ctr = ct.Register(Sub()
                                        Try
                                            If request IsNot Nothing Then request.Abort()
                                        Catch
                                        End Try
                                        Try
                                            If reqStream IsNot Nothing Then reqStream.Close()
                                        Catch
                                        End Try
                                        Try
                                            If locStream IsNot Nothing Then locStream.Close()
                                        Catch
                                        End Try
                                    End Sub)

                locStream = New IO.FileStream(t.LocalPath, IO.FileMode.Open, IO.FileAccess.Read)
                If remoteFileSize > 0 Then locStream.Seek(remoteFileSize, IO.SeekOrigin.Begin)

                reqStream = Await request.GetRequestStreamAsync()

                Dim buffer(81920) As Byte
                Dim bytesRead As Integer
                Dim totalTransferred As Long = remoteFileSize
                Dim speedTimer As Stopwatch = Stopwatch.StartNew()
                Dim speedBuffer As Long = 0

                bytesRead = Await locStream.ReadAsync(buffer, 0, buffer.Length, ct)
                While bytesRead > 0
                    ct.ThrowIfCancellationRequested()
                    Await reqStream.WriteAsync(buffer, 0, bytesRead, ct)

                    totalTransferred += bytesRead
                    speedBuffer += bytesRead

                    t.TransferredBytes = totalTransferred
                    t.Progress = (totalTransferred / localFileInfo.Length) * 100

                    If speedTimer.ElapsedMilliseconds >= 1000 Then
                        Dim actualElapsedMs = speedTimer.ElapsedMilliseconds
                        t.Speed = speedBuffer / (actualElapsedMs / 1000.0)

                        If _engine IsNot Nothing AndAlso _engine.SpeedLimitKbps > 0 Then
                            Dim expectedDelayMs = CInt((speedBuffer / 1024.0 / _engine.SpeedLimitKbps) * 1000)
                            If expectedDelayMs > actualElapsedMs Then
                                Await Task.Delay(CInt(expectedDelayMs - actualElapsedMs), ct)
                            End If
                        End If

                        speedBuffer = 0
                        speedTimer.Restart()
                    End If

                    bytesRead = Await locStream.ReadAsync(buffer, 0, buffer.Length, ct)
                End While
            End Using

            t.Progress = 100
            t.Speed = 0
            t.Status = TransferStatus.Completed
            Return True

        Catch ex As OperationCanceledException
            Throw
        Catch ex As ObjectDisposedException
            Throw New OperationCanceledException()
        Catch ex As IO.IOException
            Throw New OperationCanceledException()
        Catch ex As WebException
            If ex.Status = WebExceptionStatus.RequestCanceled Then Throw New OperationCanceledException()
            t.Status = TransferStatus.Failed
            Return False
        Catch ex As Exception
            t.Status = TransferStatus.Failed
            Return False
        Finally
            Try
                If locStream IsNot Nothing Then locStream.Dispose()
            Catch
            End Try
            Try
                If reqStream IsNot Nothing Then reqStream.Dispose()
            Catch
            End Try
        End Try
    End Function

    Private Function GetRemoteFileSize(remoteUrl As String, username As String, password As String, ct As CancellationToken) As Long
        Dim request As FtpWebRequest = Nothing
        Try
            request = CType(WebRequest.Create(New Uri(remoteUrl)), FtpWebRequest)
            request.Credentials = New NetworkCredential(username, password)
            request.Method = WebRequestMethods.Ftp.GetFileSize

            Using ctr = ct.Register(Sub()
                                        Try
                                            If request IsNot Nothing Then request.Abort()
                                        Catch
                                        End Try
                                    End Sub)
                Using response As FtpWebResponse = CType(request.GetResponse(), FtpWebResponse)
                    Return response.ContentLength
                End Using
            End Using
        Catch ex As Exception
            Return 0
        End Try
    End Function

    Private Async Function DownloadFileWithResumeAsync(t As TransferItem, ct As CancellationToken) As Task(Of Boolean)
        Dim request As FtpWebRequest = Nothing
        Dim reqStream As IO.Stream = Nothing
        Dim locStream As IO.FileStream = Nothing

        Try
            t.Status = TransferStatus.Transferring
            Dim remoteUrl As String = "ftp://" & t.Site.Host & ":" & t.Site.Port.ToString() & t.RemotePath

            Dim localFileSize As Long = 0
            Dim localFileInfo As New IO.FileInfo(t.LocalPath)

            If localFileInfo.Exists Then localFileSize = localFileInfo.Length

            request = CType(WebRequest.Create(New Uri(remoteUrl)), FtpWebRequest)
            request.Credentials = New NetworkCredential(t.Site.Username, t.Site.Password)
            request.Method = WebRequestMethods.Ftp.DownloadFile
            request.UseBinary = True

            If localFileSize > 0 Then request.ContentOffset = localFileSize

            Using ctr = ct.Register(Sub()
                                        Try
                                            If request IsNot Nothing Then request.Abort()
                                        Catch
                                        End Try
                                        Try
                                            If reqStream IsNot Nothing Then reqStream.Close()
                                        Catch
                                        End Try
                                        Try
                                            If locStream IsNot Nothing Then locStream.Close()
                                        Catch
                                        End Try
                                    End Sub)

                Dim response As FtpWebResponse = CType(Await request.GetResponseAsync(), FtpWebResponse)
                reqStream = response.GetResponseStream()
                locStream = New IO.FileStream(t.LocalPath, IO.FileMode.Append, IO.FileAccess.Write)

                Dim buffer(81920) As Byte
                Dim bytesRead As Integer
                Dim totalTransferred As Long = localFileSize
                Dim expectedTotalSize As Long = t.FileSize
                Dim speedTimer As Stopwatch = Stopwatch.StartNew()
                Dim speedBuffer As Long = 0

                bytesRead = Await reqStream.ReadAsync(buffer, 0, buffer.Length, ct)
                While bytesRead > 0
                    ct.ThrowIfCancellationRequested()
                    Await locStream.WriteAsync(buffer, 0, bytesRead, ct)

                    totalTransferred += bytesRead
                    speedBuffer += bytesRead
                    t.TransferredBytes = totalTransferred

                    If expectedTotalSize > 0 Then t.Progress = (totalTransferred / expectedTotalSize) * 100

                    If speedTimer.ElapsedMilliseconds >= 1000 Then
                        Dim actualElapsedMs = speedTimer.ElapsedMilliseconds
                        t.Speed = speedBuffer / (actualElapsedMs / 1000.0)

                        If _engine IsNot Nothing AndAlso _engine.SpeedLimitKbps > 0 Then
                            Dim expectedDelayMs = CInt((speedBuffer / 1024.0 / _engine.SpeedLimitKbps) * 1000)
                            If expectedDelayMs > actualElapsedMs Then
                                Await Task.Delay(CInt(expectedDelayMs - actualElapsedMs), ct)
                            End If
                        End If

                        speedBuffer = 0
                        speedTimer.Restart()
                    End If

                    bytesRead = Await reqStream.ReadAsync(buffer, 0, buffer.Length, ct)
                End While
            End Using

            t.Progress = 100
            t.Speed = 0
            t.Status = TransferStatus.Completed
            Return True

        Catch ex As OperationCanceledException
            Throw
        Catch ex As ObjectDisposedException
            Throw New OperationCanceledException()
        Catch ex As IO.IOException
            Throw New OperationCanceledException()
        Catch ex As WebException
            If ex.Status = WebExceptionStatus.RequestCanceled Then Throw New OperationCanceledException()
            t.Status = TransferStatus.Failed
            Return False
        Catch ex As Exception
            t.Status = TransferStatus.Failed
            Return False
        Finally
            Try
                If locStream IsNot Nothing Then locStream.Dispose()
            Catch
            End Try
            Try
                If reqStream IsNot Nothing Then reqStream.Dispose()
            Catch
            End Try
        End Try
    End Function
    ' =========================================================================================
    ' 1. ANA KUYRUK YÖNETİCİSİ (ProcessQueueAsync) - GÜNCELLENDİ
    ' =========================================================================================
    Private Async Function ProcessQueueAsync() As Task
        If _isProcessing Then Return
        _isProcessing = True

        Try
            Dim limit As Integer = 2
            Dispatcher.Invoke(Sub()
                                  If cmbConcurrentLimit?.SelectedItem IsNot Nothing Then
                                      limit = CInt(DirectCast(cmbConcurrentLimit.SelectedItem, ComboBoxItem).Content)
                                  End If
                              End Sub)

            If _transferSemaphore Is Nothing Then
                _transferSemaphore = New Threading.SemaphoreSlim(limit)
            End If

            While True
                Dim itemToProcess As TransferItem = Nothing

                ' Kuyruktaki ilk dosyayı bul ve BAŞKA BİR THREAD ALMASIN DİYE hemen "Bağlanıyor" yap
                Dispatcher.Invoke(Sub()
                                      itemToProcess = _transfers.FirstOrDefault(Function(x) x.Status = TransferStatus.Queued)
                                      If itemToProcess IsNot Nothing Then
                                          itemToProcess.Status = TransferStatus.Connecting
                                      End If
                                  End Sub)

                If itemToProcess Is Nothing Then Exit While

                Dim currentItem = itemToProcess

                ' DÜZELTME: Token'ı Semaphore beklemesinden ve Task'tan ÖNCE oluşturuyoruz.
                ' Böylece dosya kuyrukta (Connecting) beklerken bile Duraklat tuşu anında bulup müdahale edebilir!
                Dim cts As New CancellationTokenSource()
                _cancelSources(currentItem.Id) = cts

                ' Trafik polisi: Eşzamanlı limit dolduysa boş yer açılana kadar burada bekler
                Await _transferSemaphore.WaitAsync()

                Dim t = Task.Run(Async Function()
                                     Try
                                         ' Dosyaya sıra geldiğinde son kontrol: Kullanıcı ben beklerken iptal/duraklat yaptı mı?
                                         If currentItem.Status = TransferStatus.Paused OrElse currentItem.Status = TransferStatus.Cancelled Then
                                             Return ' İptal edildiyse hiç başlama, doğrudan bitiş (Finally) bloğuna git
                                         End If

                                         If currentItem.Direction = TransferDirection.Upload Then
                                             Await UploadFileWithResumeAsync(currentItem, cts.Token)
                                         Else
                                             Await DownloadFileWithResumeAsync(currentItem, cts.Token)
                                         End If
                                     Catch ex As OperationCanceledException
                                         ' Zaten Paused (Duraklatıldı) yapıldıysa durumu ezme!
                                         If currentItem.Status <> TransferStatus.Paused Then
                                             currentItem.Status = TransferStatus.Cancelled
                                         End If
                                         Log("İşlem durduruldu: " & currentItem.FileName, LogLevel.Warning)
                                     Catch ex As Exception
                                         currentItem.Status = TransferStatus.Failed
                                         Log("Hata (" & currentItem.FileName & "): " & ex.Message, LogLevel.Error)
                                     Finally
                                         ' Kanalı (Semaphore) sıradaki dosya için serbest bırak
                                         _transferSemaphore.Release()
                                         _cancelSources.Remove(currentItem.Id)

                                         Dispatcher.BeginInvoke(Sub()
                                                                    UpdateQueueStats()
                                                                    If currentItem.Status = TransferStatus.Completed Then
                                                                        If currentItem.Direction = TransferDirection.Download Then
                                                                            LoadLocal(_localDir)
                                                                        Else
                                                                            FireAndForget(LoadRemote(_remoteDir))
                                                                        End If
                                                                    End If
                                                                End Sub)
                                         If currentItem.Status = TransferStatus.Completed Then
                                             _trayIcon.ShowBalloonTip(3000, "Transfer Tamamlandı", currentItem.FileName & " başarıyla aktarıldı.", System.Windows.Forms.ToolTipIcon.Info)
                                         End If
                                     End Try
                                 End Function)

                _activeTransfers.Add(t)
            End While

            If _activeTransfers.Count > 0 Then Await Task.WhenAll(_activeTransfers)

        Catch ex As Exception
            Log("Kuyruk işlemcisi kritik hata: " & ex.Message, LogLevel.Error)
        Finally
            _isProcessing = False
            _activeTransfers.Clear()
        End Try
    End Function

    ' =========================================================================================
    ' SEÇİLİ OLANLARI DURDUR / DEVAM ET / İPTAL ET
    ' =========================================================================================
    Private Sub btnPauseSelected_Click(sender As Object, e As RoutedEventArgs)
        Dim selectedItems = dgTransfers.SelectedItems.Cast(Of TransferItem)().ToList()
        If selectedItems.Count = 0 Then
            Log("Lütfen duraklatmak için listeden bir dosya seçin.", LogLevel.Warning)
            Return
        End If

        For Each t In selectedItems
            If t.Status = TransferStatus.Transferring OrElse t.Status = TransferStatus.Connecting OrElse t.Status = TransferStatus.Queued Then
                t.Status = TransferStatus.Paused
                t.Speed = 0
            End If
            If _cancelSources.ContainsKey(t.Id) Then _cancelSources(t.Id).Cancel()
        Next
        UpdateQueueStats()
    End Sub

    Private Sub btnResumeSelected_Click(sender As Object, e As RoutedEventArgs)
        Dim selectedItems = dgTransfers.SelectedItems.Cast(Of TransferItem)().ToList()
        If selectedItems.Count = 0 Then
            Log("Lütfen devam ettirmek için listeden bir dosya seçin.", LogLevel.Warning)
            Return
        End If

        Dim needsProcessing As Boolean = False
        For Each t In selectedItems
            If t.Status = TransferStatus.Paused OrElse t.Status = TransferStatus.Failed OrElse t.Status = TransferStatus.Cancelled Then
                t.Status = TransferStatus.Queued
                needsProcessing = True
            End If
        Next

        UpdateQueueStats()
        If needsProcessing Then Dim _ign = Task.Run(Function() ProcessQueueAsync())
    End Sub

    Private Sub btnCancelSelected_Click(sender As Object, e As RoutedEventArgs)
        Dim selectedItems = dgTransfers.SelectedItems.Cast(Of TransferItem)().ToList()
        If selectedItems.Count = 0 Then
            Log("Lütfen iptal etmek için listeden bir dosya seçin.", LogLevel.Warning)
            Return
        End If

        For Each t In selectedItems
            If t.Status = TransferStatus.Queued OrElse t.Status = TransferStatus.Connecting OrElse t.Status = TransferStatus.Transferring Then
                t.Status = TransferStatus.Cancelled
            End If
            If _cancelSources.ContainsKey(t.Id) Then _cancelSources(t.Id).Cancel()
        Next
        UpdateQueueStats()
    End Sub

    ' =========================================================================================
    ' YENİ: TÜMÜNÜ DURDUR VE TÜMÜNÜ DEVAM ET 
    ' =========================================================================================
    Private Sub btnPauseAll_Click(sender As Object, e As RoutedEventArgs)
        Dim needsUpdate As Boolean = False
        For Each t As TransferItem In _transfers
            If t.Status = TransferStatus.Transferring OrElse t.Status = TransferStatus.Connecting OrElse t.Status = TransferStatus.Queued Then
                t.Status = TransferStatus.Paused
                t.Speed = 0
                needsUpdate = True
                If _cancelSources.ContainsKey(t.Id) Then _cancelSources(t.Id).Cancel()
            End If
        Next

        If needsUpdate Then
            Log("Tüm aktif aktarımlar duraklatıldı.", LogLevel.Info)
            UpdateQueueStats()
        End If
    End Sub

    Private Sub btnResumeAll_Click(sender As Object, e As RoutedEventArgs)
        Dim needsProcessing As Boolean = False
        For Each t As TransferItem In _transfers
            If t.Status = TransferStatus.Paused OrElse t.Status = TransferStatus.Failed OrElse t.Status = TransferStatus.Cancelled Then
                t.Status = TransferStatus.Queued
                needsProcessing = True
            End If
        Next

        If needsProcessing Then
            Log("Kuyruk yeniden başlatılıyor...", LogLevel.Info)
            UpdateQueueStats()
            Dim _ign = Task.Run(Function() ProcessQueueAsync())
        End If
    End Sub
    Private Sub ctxCopyUrl_Click(sender As Object, e As RoutedEventArgs)
        Dim item As FtpFileItem = TryCast(dgRemoteFiles.SelectedItem, FtpFileItem)
        If item Is Nothing OrElse item.IsDirectory OrElse item.Name = ".." Then Return

        Dim path As String = _remoteDir.TrimEnd("/"c) & "/" & item.Name

        ' Sunucu kök dizinlerini URL'den temizle (Örn: /public_html/resim.jpg -> /resim.jpg)
        If path.StartsWith("/public_html") Then path = path.Substring(12)
        If path.StartsWith("/httpdocs") Then path = path.Substring(9)

        ' Host başındaki ftp. kısmını temizle
        Dim host As String = _site.Host.ToLower().Replace("ftp.", "")

        ' SEO uyumlu, doğrudan HTTPS linkini oluştur
        Dim finalUrl As String = "https://" & If(host.StartsWith("www."), host, "www." & host) & path

        Clipboard.SetText(finalUrl)
        Log("Web adresi kopyalandı: " & finalUrl, LogLevel.Success)
    End Sub
    ' Hızlı Erişim ComboBox'ı için yardımcı sınıf
    Public Class DriveInfoItem
        Public Property Icon As String
        Public Property DisplayName As String
        Public Property Path As String
    End Class
End Class
