Imports System.Collections.ObjectModel

Partial Public Class SiteManagerWindow
    Inherits Window

    Private _store As SiteStore
    Private _sites As ObservableCollection(Of FtpSite)
    Public Property SelectedSite As FtpSite

    Public Sub New(store As SiteStore)
        InitializeComponent()
        _store = store
        _sites = New ObservableCollection(Of FtpSite)(_store.LoadSites())
        lstSites.ItemsSource = _sites
        
        ' Protokol seçeneklerini doldur
        cmbProtocol.Items.Add("FTP")
        cmbProtocol.Items.Add("FTPS (Implicit)")
        cmbProtocol.Items.Add("FTPS (Explicit)")
        cmbProtocol.Items.Add("SFTP")
        cmbProtocol.SelectedIndex = 0
    End Sub

    Private Sub btnNew_Click(sender As Object, e As RoutedEventArgs)
        ClearForm()
        lstSites.SelectedIndex = -1
    End Sub

    Private Sub btnEdit_Click(sender As Object, e As RoutedEventArgs)
        Dim site As FtpSite = TryCast(lstSites.SelectedItem, FtpSite)
        If site Is Nothing Then
            MessageBox.Show("Lütfen bir site seçin.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning)
            Return
        End If
        FillForm(site)
    End Sub

    Private Sub btnDelete_Click(sender As Object, e As RoutedEventArgs)
        Dim site As FtpSite = TryCast(lstSites.SelectedItem, FtpSite)
        If site Is Nothing Then Return
        If MessageBox.Show("Bu siteyi silmek istediğinizden emin misiniz?", "Sil", MessageBoxButton.YesNo, MessageBoxImage.Question) = MessageBoxResult.Yes Then
            _sites.Remove(site)
            _store.SaveSites(New List(Of FtpSite)(_sites))
            ClearForm()
        End If
    End Sub

    Private Sub btnSave_Click(sender As Object, e As RoutedEventArgs)
        If String.IsNullOrWhiteSpace(txtName.Text) Then
            MessageBox.Show("Site adı gereklidir.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error)
            Return
        End If
        
        Dim site As New FtpSite With {
            .Name = txtName.Text,
            .Host = txtHost.Text,
            .Port = CInt(If(String.IsNullOrWhiteSpace(txtPort.Text), "21", txtPort.Text)),
            .Username = txtUsername.Text,
            .Password = pbPassword.Password,
            .Protocol = cmbProtocol.SelectedIndex,
            .Comments = txtComments.Text,
            .TimeoutSeconds = 30
        }
        
        Dim existing = _sites.FirstOrDefault(Function(s) s.Name = txtName.Text)
        If existing IsNot Nothing Then
            _sites.Remove(existing)
        End If
        
        _sites.Add(site)
        _store.SaveSites(New List(Of FtpSite)(_sites))
        MessageBox.Show("Site kaydedildi.", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information)
        ClearForm()
    End Sub

    Private Sub btnConnect_Click(sender As Object, e As RoutedEventArgs)
        Dim site As FtpSite = TryCast(lstSites.SelectedItem, FtpSite)
        If site Is Nothing Then
            MessageBox.Show("Lütfen bir site seçin.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning)
            Return
        End If
        SelectedSite = site
        DialogResult = True
        Close()
    End Sub

    Private Sub btnCancel_Click(sender As Object, e As RoutedEventArgs)
        DialogResult = False
        Close()
    End Sub

    Private Sub FillForm(site As FtpSite)
        txtName.Text = site.Name
        txtHost.Text = site.Host
        txtPort.Text = site.Port.ToString()
        cmbProtocol.SelectedIndex = site.Protocol
        txtUsername.Text = site.Username
        pbPassword.Password = site.Password
        txtComments.Text = site.Comments
    End Sub

    Private Sub ClearForm()
        txtName.Clear()
        txtHost.Clear()
        txtPort.Text = "21"
        cmbProtocol.SelectedIndex = 0
        txtUsername.Clear()
        pbPassword.Clear()
        txtComments.Clear()
    End Sub

    Private Sub lstSites_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)
        Dim site As FtpSite = TryCast(lstSites.SelectedItem, FtpSite)
        If site IsNot Nothing Then FillForm(site) Else ClearForm()
    End Sub
End Class
