// File: InvertedBoolConverter.cs
// Purpose: Invert busy state for accessible action enablement in XAML.
// Symbols and line locations: see docs/code-index.md.
// Important state: conversion is stateless and accepts only Boolean values.

using System.Globalization;

namespace FieldOps.App;

/// <summary>One-way Boolean inversion converter.</summary>
public sealed class InvertedBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool boolean && !boolean;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("This converter is one-way only.");
}
