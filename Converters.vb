Imports System
Imports System.Globalization
Imports System.Windows
Imports System.Windows.Data
Imports System.Windows.Media

Public Class StringToBrushConverter
    Implements IValueConverter

    Public Function Convert(value As Object, targetType As System.Type, parameter As Object, culture As CultureInfo) As Object Implements IValueConverter.Convert
        Try
            If value Is Nothing Then Return Brushes.Gray
            Dim c As System.Windows.Media.Color = DirectCast(ColorConverter.ConvertFromString(value.ToString()), System.Windows.Media.Color)
            Return New SolidColorBrush(c)
        Catch
            Return Brushes.Gray
        End Try
    End Function

    Public Function ConvertBack(value As Object, targetType As System.Type, parameter As Object, culture As CultureInfo) As Object Implements IValueConverter.ConvertBack
        Return Nothing
    End Function
End Class

Public Class BoolToVisibilityConverter
    Implements IValueConverter

    Public Function Convert(value As Object, targetType As System.Type, parameter As Object, culture As CultureInfo) As Object Implements IValueConverter.Convert
        Dim b As Boolean = If(TypeOf value Is Boolean, CBool(value), False)
        If parameter IsNot Nothing AndAlso parameter.ToString() = "Inverse" Then b = Not b
        Return If(b, Visibility.Visible, Visibility.Collapsed)
    End Function

    Public Function ConvertBack(value As Object, targetType As System.Type, parameter As Object, culture As CultureInfo) As Object Implements IValueConverter.ConvertBack
        Return value IsNot Nothing AndAlso value.Equals(Visibility.Visible)
    End Function
End Class

Public Class TransferDirectionToColorConverter
    Implements IValueConverter

    Public Function Convert(value As Object, targetType As System.Type, parameter As Object, culture As CultureInfo) As Object Implements IValueConverter.Convert
        If value Is Nothing Then Return Brushes.Gray
        If DirectCast(value, TransferDirection) = TransferDirection.Upload Then
            Return New SolidColorBrush(DirectCast(ColorConverter.ConvertFromString("#FFB347"), System.Windows.Media.Color))
        End If
        Return New SolidColorBrush(DirectCast(ColorConverter.ConvertFromString("#1D8CF8"), System.Windows.Media.Color))
    End Function

    Public Function ConvertBack(value As Object, targetType As System.Type, parameter As Object, culture As CultureInfo) As Object Implements IValueConverter.ConvertBack
        Return Nothing
    End Function
End Class

Public Class TransferActiveToVisibilityConverter
    Implements IValueConverter

    Public Function Convert(value As Object, targetType As System.Type, parameter As Object, culture As CultureInfo) As Object Implements IValueConverter.Convert
        If value Is Nothing Then Return Visibility.Collapsed
        Dim s As TransferStatus = DirectCast(value, TransferStatus)
        Return If(s = TransferStatus.Transferring OrElse s = TransferStatus.Connecting, Visibility.Visible, Visibility.Collapsed)
    End Function

    Public Function ConvertBack(value As Object, targetType As System.Type, parameter As Object, culture As CultureInfo) As Object Implements IValueConverter.ConvertBack
        Return Nothing
    End Function
End Class

Public Class StringToVisibilityConverter
    Implements IValueConverter

    Public Function Convert(value As Object, targetType As System.Type, parameter As Object, culture As CultureInfo) As Object Implements IValueConverter.Convert
        Dim s As String = If(value IsNot Nothing, value.ToString(), "")
        Dim vis As Boolean = Not String.IsNullOrEmpty(s)
        If parameter IsNot Nothing AndAlso parameter.ToString() = "Inverse" Then vis = Not vis
        Return If(vis, Visibility.Visible, Visibility.Collapsed)
    End Function

    Public Function ConvertBack(value As Object, targetType As System.Type, parameter As Object, culture As CultureInfo) As Object Implements IValueConverter.ConvertBack
        Return Nothing
    End Function
End Class

Public Class DirectoryForegroundConverter
    Implements IValueConverter

    Public Function Convert(value As Object, targetType As System.Type, parameter As Object, culture As CultureInfo) As Object Implements IValueConverter.Convert
        If TypeOf value Is Boolean AndAlso CBool(value) Then
            Return New SolidColorBrush(DirectCast(ColorConverter.ConvertFromString("#7CC5FF"), System.Windows.Media.Color))
        End If
        Return New SolidColorBrush(DirectCast(ColorConverter.ConvertFromString("#E8EDF5"), System.Windows.Media.Color))
    End Function

    Public Function ConvertBack(value As Object, targetType As System.Type, parameter As Object, culture As CultureInfo) As Object Implements IValueConverter.ConvertBack
        Return Nothing
    End Function
End Class

Public Class BoolToFontWeightConverter
    Implements IValueConverter

    Public Function Convert(value As Object, targetType As System.Type, parameter As Object, culture As CultureInfo) As Object Implements IValueConverter.Convert
        Return If(TypeOf value Is Boolean AndAlso CBool(value), FontWeights.Bold, FontWeights.Normal)
    End Function

    Public Function ConvertBack(value As Object, targetType As System.Type, parameter As Object, culture As CultureInfo) As Object Implements IValueConverter.ConvertBack
        Return Nothing
    End Function
End Class
