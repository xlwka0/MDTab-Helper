using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace MDHelper;

public class SimpleKeybindTest
{
    public static void TestKeyPress(object sender, KeyEventArgs e)
    {
        try
        {
            Debug.WriteLine($"Key pressed: {e.Key}");
            
            if (e.Key == Key.F1)
            {
                Debug.WriteLine("F1 detected - starting simple test");
                MessageBox.Show("F1 detected! This is a test.", "Keybind Test", MessageBoxButton.OK, MessageBoxImage.Information);
                Debug.WriteLine("Test completed successfully");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Crash in key handler: {ex.Message}");
            MessageBox.Show($"Error: {ex.Message}", "Crash", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
