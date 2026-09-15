using System;
using System.Reactive.Concurrency;
using System.Threading.Tasks;
using System.Windows.Input;
using Prism.Navigation;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Shiny;

namespace GpsSync;

public class RegisterViewModel : ViewModel
{
	private readonly IBackendClient backend;

	[Reactive] public string Email { get; set; } = string.Empty;
	[Reactive] public string Password { get; set; } = string.Empty;
	[Reactive] public string ConfirmPassword { get; set; } = string.Empty;
	[Reactive] public string ErrorMessage { get; set; } = string.Empty;
	[Reactive] public bool HasError { get; set; }

	public ICommand Register { get; }
	public ICommand NavigateBack => ReactiveCommand.CreateFromTask(() => base.Navigation.GoBackAsync());

	public RegisterViewModel(BaseServices services, IBackendClient backend)
		: base(services)
	{
		this.backend = backend;
		this.WhenAnyValue(x => x.ErrorMessage).Subscribe(msg => HasError = !string.IsNullOrEmpty(msg));

		Register = ReactiveCommand.CreateFromTask((Func<Task>)async delegate
		{
			ErrorMessage = string.Empty;

			if (string.IsNullOrWhiteSpace(Email))
			{
				ErrorMessage = "Please enter your email address.";
				return;
			}
			if (string.IsNullOrWhiteSpace(Password) || Password.Length < 6)
			{
				ErrorMessage = "Password must be at least 6 characters.";
				return;
			}
			if (Password != ConfirmPassword)
			{
				ErrorMessage = "Passwords do not match.";
				return;
			}

			base.IsBusy = true;
			try
			{
				// Server creates the user AND the profile row (see AuthController.Register).
				await this.backend.RegisterAsync(Email.Trim(), Password);
				await base.Dialogs.Alert("Account created! You can now sign in.", "Success");
				await base.Navigation.GoBackAsync();
			}
			catch (Exception ex)
			{
				ErrorMessage = ToFriendlyError(ex.Message);
				System.Diagnostics.Debug.WriteLine("Register error: " + ex.Message);
			}
			finally
			{
				base.IsBusy = false;
			}
		}, (IObservable<bool>?)null, (IScheduler?)null);
	}

	private static string ToFriendlyError(string message)
	{
		string text = message.ToLowerInvariant();
		if (text.Contains("user already registered") || text.Contains("already exists") || text.Contains("already registered"))
			return "An account with this email already exists.";
		if (text.Contains("password should be at least") || text.Contains("weak_password") || text.Contains("should be at least"))
			return "Password must be at least 6 characters.";
		if (text.Contains("invalid email") || text.Contains("unable to validate"))
			return "Please enter a valid email address.";
		if (text.Contains("signup") && text.Contains("disabled"))
			return "New account registration is currently disabled.";
		if (text.Contains("network") || text.Contains("unable to connect") || text.Contains("timeout"))
			return "Connection failed. Check your internet and try again.";
		return string.IsNullOrWhiteSpace(message) ? "Something went wrong. Please try again." : message;
	}
}
