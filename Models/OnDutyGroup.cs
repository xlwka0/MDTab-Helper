using System.Collections.ObjectModel;

namespace MDHelper.Models;

public class OnDutyGroup
{
    public string Grad { get; set; } = string.Empty;
    public string GradUpper => Grad.ToUpperInvariant();
    public string GradColor { get; set; } = "#00E5FF";
    public int GradOrder { get; set; } = 99;
    public ObservableCollection<OnDutyDoctor> Doctors { get; set; } = new();
    public int Count => Doctors.Count;
    public string HeaderDisplay => $"{GradUpper} • {Count}";
}
