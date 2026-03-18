Imports System.Windows
Imports System.Windows.Controls
Imports System.Windows.Forms

Public Class ChmodWindow
    Inherits Window ' BÜYÜ DÜĞÜMÜ BURASI: UserControl olan sınıfı Window sınıfına miras bıraktık!

    Public Property NumericPermissions As String = "644"
    Private _isUpdating As Boolean = False

    Public Sub New(fileName As String, currentPerms As String)
        InitializeComponent()
        lblFileName.Text = "Dosya: " & fileName

        NumericPermissions = If(String.IsNullOrWhiteSpace(currentPerms), "644", currentPerms)
        txtNumeric.Text = NumericPermissions
    End Sub

    Private Sub CheckBox_Changed(sender As Object, e As RoutedEventArgs)
        If _isUpdating Then Return
        Dim total As Integer = 0
        ' Sahip
        If chkOwnerRead.IsChecked Then total += 400
        If chkOwnerWrite.IsChecked Then total += 200
        If chkOwnerExecute.IsChecked Then total += 100
        ' Grup
        If chkGroupRead.IsChecked Then total += 40
        If chkGroupWrite.IsChecked Then total += 20
        If chkGroupExecute.IsChecked Then total += 10
        ' Genel
        If chkPublicRead.IsChecked Then total += 4
        If chkPublicWrite.IsChecked Then total += 2
        If chkPublicExecute.IsChecked Then total += 1

        _isUpdating = True
        txtNumeric.Text = total.ToString("D3")
        NumericPermissions = txtNumeric.Text
        _isUpdating = False
    End Sub

    Private Sub txtNumeric_TextChanged(sender As Object, e As TextChangedEventArgs)
        If _isUpdating Then Return

        If txtNumeric.Text.Length = 3 Then
            Dim valOwner As Integer
            Dim valGroup As Integer
            Dim valPublic As Integer

            ' Metin kutusundaki 3 rakamı (örn: 7, 5, 5) ayrı ayrı sayıya çeviriyoruz
            If Integer.TryParse(txtNumeric.Text.Substring(0, 1), valOwner) AndAlso
               Integer.TryParse(txtNumeric.Text.Substring(1, 1), valGroup) AndAlso
               Integer.TryParse(txtNumeric.Text.Substring(2, 1), valPublic) Then

                _isUpdating = True

                ' Bitsel AND işlemi (Sayıları ayırma)
                chkOwnerRead.IsChecked = ((valOwner And 4) = 4)
                chkOwnerWrite.IsChecked = ((valOwner And 2) = 2)
                chkOwnerExecute.IsChecked = ((valOwner And 1) = 1)

                chkGroupRead.IsChecked = ((valGroup And 4) = 4)
                chkGroupWrite.IsChecked = ((valGroup And 2) = 2)
                chkGroupExecute.IsChecked = ((valGroup And 1) = 1)

                chkPublicRead.IsChecked = ((valPublic And 4) = 4)
                chkPublicWrite.IsChecked = ((valPublic And 2) = 2)
                chkPublicExecute.IsChecked = ((valPublic And 1) = 1)

                NumericPermissions = txtNumeric.Text
                _isUpdating = False
            End If
        End If
    End Sub

    Private Sub btnApply_Click(sender As Object, e As RoutedEventArgs)
        ' Sadece DialogResult ataması yeterlidir, pencere otomatik kapanır!
        DialogResult = True
    End Sub

    Private Sub btnCancel_Click(sender As Object, e As RoutedEventArgs)
        ' Aynı şekilde burada da Close() kullanmıyoruz.
        DialogResult = False
    End Sub
End Class