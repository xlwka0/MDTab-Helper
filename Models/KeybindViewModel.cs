using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;

namespace MDHelper.Models;

public class KeybindViewModel : INotifyPropertyChanged
{
    public KeybindEntry Entry { get; }

    public string Name
    {
        get => Entry.Name;
        set { Entry.Name = value; OnPropertyChanged(); }
    }

    public bool Enabled
    {
        get => Entry.Enabled;
        set { Entry.Enabled = value; OnPropertyChanged(); }
    }

    public string KeyDisplay
    {
        get
        {
            var parts = new List<string>();
            if ((Entry.HotkeyModifiers & 0x0002) != 0) parts.Add("Ctrl");
            if ((Entry.HotkeyModifiers & 0x0001) != 0) parts.Add("Alt");
            if ((Entry.HotkeyModifiers & 0x0004) != 0) parts.Add("Shift");
            if ((Entry.HotkeyModifiers & 0x0008) != 0) parts.Add("Win");

            parts.Add(VkToKeyName(Entry.HotkeyVirtualKey));
            return string.Join(" + ", parts);
        }
    }

    public string ScriptPreview
    {
        get
        {
            var firstLine = Entry.Script?.Split('\n').FirstOrDefault() ?? "";
            if (firstLine.Length > 40) firstLine = firstLine[..40] + "...";
            return firstLine;
        }
    }

    public KeybindViewModel(KeybindEntry entry)
    {
        Entry = entry;
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Enabled));
        OnPropertyChanged(nameof(KeyDisplay));
        OnPropertyChanged(nameof(ScriptPreview));
    }

    public static string VkToKeyName(int vk)
    {
        var key = KeyInterop.KeyFromVirtualKey(vk);
        var name = key.ToString();
        if (name.Length == 2 && name[0] == 'D' && char.IsDigit(name[1]))
            return name[1].ToString();
        if (name.StartsWith("NumPad", StringComparison.OrdinalIgnoreCase) && name.Length == 7 && char.IsDigit(name[6]))
            return $"Num {name[6]}";
        return name switch
        {
            "Next" => "PageDown",
            "PageDown" => "PageDown",
            "Prior" => "PageUp",
            "PageUp" => "PageUp",
            "Snapshot" => "PrintScreen",
            "Scroll" => "ScrollLock",
            "Return" => "Enter",
            "Back" => "Backspace",
            "Capital" => "CapsLock",
            "Escape" => "Esc",
            "OemQuestion" => "/",
            "OemPeriod" => ".",
            "OemComma" => ",",
            "OemMinus" => "-",
            "OemPlus" => "+",
            "OemTilde" => "`",
            "Oem3" => "`",
            "OemOpenBrackets" => "[",
            "OemCloseBrackets" => "]",
            "OemPipe" => "\\",
            "OemQuotes" => "'",
            "OemSemicolon" => ";",
            "Divide" => "Num /",
            "Multiply" => "Num *",
            "Subtract" => "Num -",
            "Add" => "Num +",
            "Decimal" => "Num .",
            _ => name
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string propertyName = "") => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
