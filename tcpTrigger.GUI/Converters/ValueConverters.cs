using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using tcpTrigger.GUI.Models;

namespace tcpTrigger.GUI.Converters
{
    [ValueConversion(typeof(bool), typeof(Visibility))]
    internal sealed class BoolToVisibilityConverter : IValueConverter
    {
        public Visibility TrueValue { get; set; }
        public Visibility FalseValue { get; set; }

        public BoolToVisibilityConverter()
        {
            // Set defaults.
            TrueValue = Visibility.Visible;
            FalseValue = Visibility.Collapsed;
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is bool))
            {
                return null;
            }

            return (bool)value ? TrueValue : FalseValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (Equals(value, TrueValue))
            {
                return true;
            }
            if (Equals(value, FalseValue))
            {
                return false;
            }

            return null;
        }
    }

    internal class VisibilityToEnabledConverter : IValueConverter
    {
        // Return false if Visibility.Visible, otherwise return true.
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility visibility && targetType == typeof(bool))
            {
                return visibility != Visibility.Visible;
            }
            return DependencyProperty.UnsetValue; // Indicate conversion failure
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    internal class NotificationTypeToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is NotificationType type)
            {
                switch (type)
                {
                    case NotificationType.LogFile:
                        return Application.Current.FindResource("icon.notepad");
                    case NotificationType.Command:
                        return Application.Current.FindResource("icon.app");
                    case NotificationType.Email:
                        return Application.Current.FindResource("icon.email");
                    default:
                        return null;
                }
            }

            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    // Shortens a list of strings for display, such as an ignore rule's sources.
    // A list longer than MaxItems keeps its first (MaxItems - 1) items, leaving room
    // for a "+N more" line, so the list never takes more than MaxItems lines.
    // With ConverterParameter=more, returns that "+N more" text instead, or an empty string.
    // A list that's just the "any" keyword shows AnyText instead, if it's set,
    // or nothing at all if HideAny is true.
    // As a MultiBinding of an ignore rule's services and then its destinations, "any" services
    // show AnyTextWithDestinations when the rule has destination addresses, since those are
    // listed above the services.
    internal sealed class ListPreviewConverter : IValueConverter, IMultiValueConverter
    {
        public int MaxItems { get; set; } = 6;

        // Such as "Any IP address".
        public string AnyText { get; set; }

        // For lists that are "any" in most rules, such as destination addresses.
        public bool HideAny { get; set; }

        // Such as "Any port", under a rule's destination addresses.
        public string AnyTextWithDestinations { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Preview(value, parameter, AnyText);
        }

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            List<string> destinations = values.Length > 1 ? values[1] as List<string> : null;
            bool hasDestinations = destinations != null
                && destinations.Count > 0
                && !(destinations.Count == 1 && destinations[0] == "any");
            return Preview(values[0], parameter, hasDestinations ? AnyTextWithDestinations : AnyText);
        }

        private object Preview(object value, object parameter, string anyText)
        {
            List<string> items = value as List<string>;
            bool isMoreText = (parameter as string) == "more";

            bool isAny = items != null && items.Count == 1 && items[0] == "any";
            if (isAny && HideAny)
            {
                return isMoreText ? (object)string.Empty : new List<string>();
            }
            if (isAny && anyText != null)
            {
                return isMoreText ? (object)string.Empty : new List<string> { anyText };
            }

            if (items == null || items.Count <= MaxItems)
            {
                return isMoreText ? string.Empty : value;
            }

            int shown = MaxItems - 1;
            return isMoreText
                ? (object)$"+{items.Count - shown} more"
                : items.Take(shown).ToList();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}