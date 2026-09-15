using System.Windows.Input;
using Microsoft.Maui.Graphics;
using ReactiveUI;

namespace GpsSync;

public class EngineerStatusItem
{
	public string UserId { get; set; } = string.Empty;

	public string DisplayName { get; set; } = string.Empty;

	public string Initials { get; set; } = string.Empty;

	public string StatusText { get; set; } = "Offline";

	public string LastSeen { get; set; } = "No data";

	public Color StatusColor { get; set; } = Colors.Gray;

	public ICommand? AssignCommand { get; set; }

	public ICommand? RenameCommand { get; set; }

	public ICommand? RemoveCommand { get; set; }
}
