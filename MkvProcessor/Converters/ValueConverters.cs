using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using MkvProcessor.Models;

namespace MkvProcessor.Converters;

/// <summary>
/// Converts boolean, int, or object to visibility
/// - bool: true = Visible
/// - int: > 0 = Visible
/// - object: not null = Visible
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var invert = parameter?.ToString() == "Invert";

        var boolValue = value switch
        {
            bool b => b,
            int i => i > 0,
            _ => value != null
        };

        if (invert) boolValue = !boolValue;
        return boolValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts FileStatus to display color
/// </summary>
public class StatusToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is FileStatus status)
        {
            return status switch
            {
                FileStatus.Pending => new SolidColorBrush(Colors.Gray),
                FileStatus.Processing => new SolidColorBrush(Colors.DodgerBlue),
                FileStatus.Complete => new SolidColorBrush(Colors.Green),
                FileStatus.Error => new SolidColorBrush(Colors.Red),
                FileStatus.Skipped => new SolidColorBrush(Colors.Orange),
                _ => new SolidColorBrush(Colors.Gray)
            };
        }
        return new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts FileStatus to display string
/// </summary>
public class StatusToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is FileStatus status)
        {
            return status switch
            {
                FileStatus.Pending => "Pending",
                FileStatus.Processing => "Processing...",
                FileStatus.Complete => "Complete",
                FileStatus.Error => "Error",
                FileStatus.Skipped => "Skipped",
                _ => "Unknown"
            };
        }
        return "Unknown";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts ContentType enum to display string
/// </summary>
public class ContentTypeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ContentType contentType)
        {
            return contentType switch
            {
                ContentType.TvShow => "TV Shows",
                ContentType.Movie => "Movies",
                _ => value.ToString() ?? ""
            };
        }
        return value?.ToString() ?? "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts AudioMode enum to display string
/// </summary>
public class AudioModeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is AudioMode audioMode)
        {
            return audioMode switch
            {
                AudioMode.Dual => "Normalized + Original (dual track)",
                AudioMode.Original => "Original only",
                AudioMode.Normalized => "Normalized only",
                _ => value.ToString() ?? ""
            };
        }
        return value?.ToString() ?? "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Formats quality preset for display
/// </summary>
public class QualityPresetConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is QualityPreset preset)
        {
            return $"{preset.Name} ({preset.Description})";
        }
        return value?.ToString() ?? "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Inverts a boolean value
/// </summary>
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool b && !b;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool b && !b;
    }
}

/// <summary>
/// Converts a collection of strings to a single newline-separated string
/// </summary>
public class StringCollectionToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is IEnumerable<string> lines)
        {
            return string.Join(Environment.NewLine, lines);
        }
        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts MatchConfidence to background color for visual indicator
/// </summary>
public class ConfidenceToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is MatchConfidence confidence)
        {
            return confidence switch
            {
                MatchConfidence.High => new SolidColorBrush(Color.FromRgb(34, 139, 34)),    // Forest Green
                MatchConfidence.Medium => new SolidColorBrush(Color.FromRgb(218, 165, 32)), // Goldenrod
                MatchConfidence.Low => new SolidColorBrush(Color.FromRgb(255, 140, 0)),     // Dark Orange
                MatchConfidence.None => new SolidColorBrush(Color.FromRgb(220, 20, 60)),    // Crimson
                _ => new SolidColorBrush(Colors.Gray)
            };
        }
        return new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts MatchConfidence to display string
/// </summary>
public class ConfidenceToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is MatchConfidence confidence)
        {
            return confidence switch
            {
                MatchConfidence.High => "High",
                MatchConfidence.Medium => "Medium",
                MatchConfidence.Low => "Low",
                MatchConfidence.None => "None",
                _ => "Unknown"
            };
        }
        return "Unknown";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts NamingFormat enum to display string with example
/// </summary>
public class NamingFormatConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is NamingFormat format)
        {
            return format switch
            {
                NamingFormat.Standard => "Standard (01x01)",
                NamingFormat.Scene => "Scene (S01E01)",
                _ => value.ToString() ?? ""
            };
        }
        return value?.ToString() ?? "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
