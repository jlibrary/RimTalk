using System.Collections.Generic;
using Verse;

namespace RimTalk.Prompt;

/// <summary>
/// Custom variable type.
/// </summary>
public enum CustomVariableType
{
    /// <summary>Static text</summary>
    Static,
    /// <summary>Dropdown selection (choose from predefined options)</summary>
    Select
}

/// <summary>
/// Player custom variable template - maps complex expressions to simple variable names.
/// Usage syntax: {{user.variablename}}
/// </summary>
public class CustomVariable : IExposable
{
    /// <summary>Variable name (e.g., "mygreeting", used as {{user.mygreeting}})</summary>
    public string Name = "";
    
    /// <summary>Variable description (for player reference)</summary>
    public string Description = "";
    
    /// <summary>Variable type</summary>
    public CustomVariableType Type = CustomVariableType.Static;
    
    /// <summary>Static value (used when Type is Static)</summary>
    public string StaticValue = "";
    
    /// <summary>
    /// Option list (used when Type is Select).
    /// Players can choose the current value from these options in the UI.
    /// </summary>
    public List<string> Options = new();
    
    /// <summary>Currently selected option index (used when Type is Select)</summary>
    public int SelectedIndex;

    public CustomVariable()
    {
    }

    public CustomVariable(string name, string value)
    {
        Name = name;
        StaticValue = value;
        Type = CustomVariableType.Static;
    }

    public CustomVariable(string name, List<string> options, int selectedIndex = 0)
    {
        Name = name;
        Options = options ?? new List<string>();
        SelectedIndex = selectedIndex;
        Type = CustomVariableType.Select;
    }

    /// <summary>Gets the current value</summary>
    public string GetValue()
    {
        return Type switch
        {
            CustomVariableType.Static => StaticValue ?? "",
            CustomVariableType.Select => (SelectedIndex >= 0 && SelectedIndex < Options.Count)
                ? Options[SelectedIndex]
                : "",
            _ => ""
        };
    }

    /// <summary>Sets the current value (for Static type)</summary>
    public void SetValue(string value)
    {
        if (Type == CustomVariableType.Static)
        {
            StaticValue = value ?? "";
        }
    }

    /// <summary>Selects an option (for Select type)</summary>
    public void SelectOption(int index)
    {
        if (Type == CustomVariableType.Select && index >= 0 && index < Options.Count)
        {
            SelectedIndex = index;
        }
    }

    /// <summary>Adds an option (for Select type)</summary>
    public void AddOption(string option)
    {
        if (Type == CustomVariableType.Select)
        {
            Options.Add(option ?? "");
        }
    }

    /// <summary>Removes an option (for Select type)</summary>
    public void RemoveOption(int index)
    {
        if (Type == CustomVariableType.Select && index >= 0 && index < Options.Count)
        {
            Options.RemoveAt(index);
            if (SelectedIndex >= Options.Count)
            {
                SelectedIndex = Options.Count - 1;
            }
            if (SelectedIndex < 0)
            {
                SelectedIndex = 0;
            }
        }
    }

    public void ExposeData()
    {
        Scribe_Values.Look(ref Name, "name", "");
        Scribe_Values.Look(ref Description, "description", "");
        Scribe_Values.Look(ref Type, "type", CustomVariableType.Static);
        Scribe_Values.Look(ref StaticValue, "staticValue", "");
        Scribe_Collections.Look(ref Options, "options", LookMode.Value);
        Scribe_Values.Look(ref SelectedIndex, "selectedIndex", 0);
        
        Options ??= new List<string>();
    }

    public override string ToString()
    {
        return $"{{{{user.{Name}}}}} = {GetValue()}";
    }
}