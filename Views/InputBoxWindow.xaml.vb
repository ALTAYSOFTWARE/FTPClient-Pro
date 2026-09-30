Partial Public Class InputBoxWindow
    Inherits Window

    Public Property Result As String

    Public Sub New(prompt As String, title As String, defaultValue As String)
        InitializeComponent()
        Me.Title = title
        lblPrompt.Text = prompt
        txtInput.Text = defaultValue
        txtInput.Focus()
        txtInput.SelectAll()
    End Sub

    Private Sub btnOK_Click(sender As Object, e As RoutedEventArgs)
        Result = txtInput.Text
        DialogResult = True
        Close()
    End Sub

    Private Sub btnCancel_Click(sender As Object, e As RoutedEventArgs)
        DialogResult = False
        Close()
    End Sub

    Private Sub txtInput_KeyDown(sender As Object, e As KeyEventArgs)
        If e.Key = Key.Return Then
            btnOK_Click(Nothing, Nothing)
        ElseIf e.Key = Key.Escape Then
            btnCancel_Click(Nothing, Nothing)
        End If
    End Sub
End Class
