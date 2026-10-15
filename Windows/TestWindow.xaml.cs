using System.Diagnostics;
using System.Windows;

namespace MDHelper.Windows;

public partial class TestWindow : Window
{
    public TestWindow()
    {
        InitializeComponent();
        
        StatusText.Text = "Test window - keybind functionality removed";
    }
    
    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        TestTextBox.Text = "";
        StatusText.Text = "Cleared.";
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
    }
}
