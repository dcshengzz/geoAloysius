using System;
using System.Reactive.Concurrency;
using System.Threading.Tasks;
using System.Windows.Input;
using Prism.Navigation;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Shiny;
using Supabase;

namespace GpsSync;

public class RegisterViewModel : ViewModel
{
	private readonly Supabase.Client supabase;

	[Reactive] public string Email { get; set; } = string.Empty;
	[Reactive] public string Password { get; set; } = string.Empty;
	[Reactive] public string ConfirmPassword { get; set; } = string.Empty;
	[Reactive] public string ErrorMessage { get; set; } = string.Empty;
	[Reactive] public bool HasError { get; set; }

	public ICommand Register { get; }
	public ICommand NavigateBack => ReactiveCommand.CreateFromTask(() => base.Navigation.GoBackAsync());

	public RegisterViewModel(BaseServices services, Supabase.Client supabase)
		: base(services)
	{
		this.supabase = supabase;
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
				var newUser = (await this.supabase.Auth.SignUp(Email.Trim(), Password))?.User;
				if (newUser != null)
				{
					try
					{
						await this.supabase.From<ProfileRecord>().Insert(new ProfileRecord
						{
							UserId = newUser.Id ?? string.Empty,
							Email = newUser.Email,
							IsAdmin = false,
							IsPunchedIn = false
						});
					}
					catch { }
					await base.Dialogs.Alert("Account created! Check your email to confirm before signing in.", "Success");
					await base.Navigation.GoBackAsync();
				}
				else
				{
					ErrorMessage = "Registration failed. Please try again.";
				}
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
