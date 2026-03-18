Imports System
Imports System.Windows
Imports System.Globalization
Imports System.Threading

Partial Public Class App
    Inherits Application

    Protected Overrides Sub OnStartup(e As StartupEventArgs)
        MyBase.OnStartup(e)
        Thread.CurrentThread.CurrentCulture = New CultureInfo("tr-TR")
        Thread.CurrentThread.CurrentUICulture = New CultureInfo("tr-TR")
    End Sub

End Class
