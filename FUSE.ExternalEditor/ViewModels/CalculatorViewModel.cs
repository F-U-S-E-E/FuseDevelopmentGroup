using System;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fuse.ExternalEditor.Logic;

namespace Fuse.ExternalEditor.ViewModels;

/// <summary>The calculator panel: evaluates an arithmetic expression (+ - * / ^, parens).</summary>
public partial class CalculatorViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _expression = string.Empty;

    [ObservableProperty]
    private string _result = string.Empty;

    [RelayCommand]
    private void Evaluate()
    {
        try
        {
            Result = ExpressionEvaluator.Evaluate(Expression).ToString("0.######", CultureInfo.InvariantCulture);
        }
        catch (Exception e)
        {
            Result = "Error: " + e.Message;
        }
    }
}
