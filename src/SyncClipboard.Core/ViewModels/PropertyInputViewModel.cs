using CommunityToolkit.Mvvm.ComponentModel;

namespace SyncClipboard.Core.ViewModels;

public partial class PropertyInputViewModel : ObservableObject
{
    [ObservableProperty]
    private string propertyName = "";

    [ObservableProperty]
    private string displayName = "";

    [ObservableProperty]
    private Type? propertyType;

    [ObservableProperty]
    private PropertyInputType inputType = PropertyInputType.Text;

    [ObservableProperty]
    private string value = "";

    [ObservableProperty]
    private bool boolValue = false;

    [ObservableProperty]
    private double numericValue = 0.0;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private string? description;

    public bool IsText => InputType == PropertyInputType.Text;
    public bool IsPassword => InputType == PropertyInputType.Password;
    public bool IsInteger => InputType == PropertyInputType.Integer;
    public bool IsDecimal => InputType == PropertyInputType.Decimal;
    public bool IsBoolean => InputType == PropertyInputType.Boolean;
    public bool IsNumeric => IsInteger || IsDecimal;

    public bool IsValid
    {
        get
        {
            return InputType switch
            {
                PropertyInputType.Boolean => true,
                PropertyInputType.Integer => ValidateIntegerValue(),
                PropertyInputType.Decimal => true, // double 类型始终有效
                _ => true
            };
        }
    }

    private bool ValidateIntegerValue()
    {
        if (PropertyType == null) return false;

        var baseType = Nullable.GetUnderlyingType(PropertyType) ?? PropertyType;
        var roundedValue = Math.Round(NumericValue);

        // 对于无符号整型，检查是否为非负数且在范围内
        if (baseType == typeof(uint))
        {
            return roundedValue >= uint.MinValue && roundedValue <= uint.MaxValue;
        }
        if (baseType == typeof(ulong))
        {
            return roundedValue >= ulong.MinValue && roundedValue <= (double)ulong.MaxValue;
        }
        if (baseType == typeof(ushort))
        {
            return roundedValue >= ushort.MinValue && roundedValue <= ushort.MaxValue;
        }

        // 对于有符号整型，检查范围
        if (baseType == typeof(int))
        {
            return roundedValue >= int.MinValue && roundedValue <= int.MaxValue;
        }
        if (baseType == typeof(long))
        {
            return roundedValue >= long.MinValue && roundedValue <= long.MaxValue;
        }
        if (baseType == typeof(short))
        {
            return roundedValue >= short.MinValue && roundedValue <= short.MaxValue;
        }

        return false;
    }

    public object? GetTypedValue()
    {
        return InputType switch
        {
            PropertyInputType.Boolean => BoolValue,
            PropertyInputType.Integer => ConvertToInteger(),
            PropertyInputType.Decimal => NumericValue,
            _ => Value
        };
    }

    private double? ConvertToInteger()
    {
        if (PropertyType == null) return null;

        var baseType = Nullable.GetUnderlyingType(PropertyType) ?? PropertyType;
        var roundedValue = Math.Round(NumericValue);

        if (baseType == typeof(int)) return (int)roundedValue;
        if (baseType == typeof(uint)) return (uint)Math.Max(0, roundedValue);
        if (baseType == typeof(long)) return (long)roundedValue;
        if (baseType == typeof(ulong)) return (ulong)Math.Max(0, roundedValue);
        if (baseType == typeof(short)) return (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, roundedValue));
        if (baseType == typeof(ushort)) return (ushort)Math.Max(0, Math.Min(ushort.MaxValue, roundedValue));

        return null;
    }

    public void SetTypedValue(object? value)
    {
        switch (InputType)
        {
            case PropertyInputType.Boolean:
                SetBooleanValue(value);
                break;
            case PropertyInputType.Integer:
                SetIntegerValue(value);
                break;
            case PropertyInputType.Decimal:
                SetDecimalValue(value);
                break;
            default:
                SetTextValue(value);
                break;
        }
    }

    private void SetBooleanValue(object? value)
    {
        BoolValue = value is bool b && b;
    }

    private void SetIntegerValue(object? value)
    {
        if (value == null)
        {
            Value = "";
            NumericValue = 0.0;
            return;
        }

        Value = value.ToString() ?? "";
        if (double.TryParse(Value, out var intNumeric))
        {
            NumericValue = intNumeric;
        }
    }

    private void SetDecimalValue(object? value)
    {
        if (value == null)
        {
            Value = "";
            NumericValue = 0.0;
            return;
        }

        if (value is float f)
        {
            Value = f.ToString("F2");
            NumericValue = f;
            return;
        }

        if (value is double d)
        {
            Value = d.ToString("F2");
            NumericValue = d;
            return;
        }

        if (value is decimal dec)
        {
            Value = dec.ToString("F2");
            NumericValue = (double)dec;
            return;
        }

        Value = value.ToString() ?? "";
        if (double.TryParse(Value, out var decNumeric))
        {
            NumericValue = decNumeric;
        }
    }

    private void SetTextValue(object? value)
    {
        Value = value?.ToString() ?? "";
    }
}