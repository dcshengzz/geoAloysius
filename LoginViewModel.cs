using System;
using System.Reactive.Concurrency;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Maui.Storage;
using Prism.Navigation;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Shiny;

namespace GpsSync;

public class LoginViewModel : ViewModel
{
	private readonly IBackendClient backend;

	[Reactive]
	public string Email { get; set; } = string.Empty;

	[Reactive]
	public string Password { get; set; } = string.Empty;

		[Reactive]
	public string ErrorMessage { get; set; } = string.Empty;

	[Reactive]
	public bool HasError { get; set; }

	[Reactive]
	public string VerifiedMessage { get; set; } = string.Empty;

	[Reactive]
	public bool IsVerified { get; set; }

	[Reactive]
	public string DisplacedMessage { get; set; } = string.Empty;

	[Reactive]
	public bool IsDisplaced { get; set; }

	public ICommand Login { get; }

	public ICommand Register { get; }

	public ICommand ForgotPassword { get; }

	public LoginViewModel(BaseServices services, IBackendClient backend)
		: base(services)
	{
		this.backend = backend;
		this.WhenAnyValue((LoginViewModel x) => x.ErrorMessage).Subscribe(delegate(string msg)
		{
			HasError = !string.IsNullOrEmpty(msg);
		});
		Login = ReactiveCommand.CreateFromTask((Func<Task>)async delegate
		{
			ErrorMessage = string.Empty;
			IsDisplaced = false;
			if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
			{
				ErrorMessage = "Please enter your email address and password.";
				return;
			}
			base.IsBusy = true;
			try
			{
				AuthResult auth = await this.backend.LoginAsync(Email.Trim(), Password);
				if (auth?.User != null)
				{
					// Single-device enforcement for non-admin accounts
					try
					{
						if (!auth.User.IsAdmin)
						{
							var profile = await this.backend.GetProfileAsync(auth.User.Id);
							string storedToken = await SecureStorage.Default.GetAsync("device.session_token");
							// Another device is active — ask the user before displacing it
							if (profile != null && !string.IsNullOrEmpty(profile.ActiveDeviceToken)
								&& !string.IsNullOrEmpty(storedToken) && storedToken != profile.ActiveDeviceToken)
							{
								bool proceed = await Dialogs.Confirm(
									"This account is currently signed in on another device. Signing in here will sign out that device. Do you want to continue?",
									"Account Active on Another Device");
								if (!proceed)
								{
									await this.backend.LogoutAsync();
									return;
								}
								// User confirmed — fall through to overwrite the token below
							}
							// Register this device as the sole active device
							string newToken = Guid.NewGuid().ToString();
							await SecureStorage.Default.SetAsync("device.session_token", newToken);
							await this.backend.UpdateProfileAsync(auth.User.Id, activeDeviceToken: newToken);
						}
					}
					catch
					{
						// Fail safe: cannot verify device authorization — block login
						await this.backend.LogoutAsync();
						ErrorMessage = "Unable to verify device authorization. Check your connection and try again.";
						return;
					}

					await base.Navigation.NavigateAsync("/NavigationPage/MainPage");
				}
			}
			catch (Exception ex)
			{
				Exception ex2 = ex;
				ErrorMessage = ToFriendlyError(ex2.Message);
			}
			finally
			{
				base.IsBusy = false;
			}
		}, (IObservable<bool>?)null, (IScheduler?)null);
		Register = ReactiveCommand.CreateFromTask(() => base.Navigation.NavigateAsync("RegisterPage"), (IObservable<bool>?)null, (IScheduler?)null);
		ForgotPassword = ReactiveCommand.CreateFromTask((Func<Task>)async delegate
		{
			NavigationParameters p = new NavigationParameters();
			if (!string.IsNullOrWhiteSpace(Email))
			{
				p.Add("email", Email);
			}
			await base.Navigation.NavigateAsync("ForgotPasswordPage", p);
		}, (IObservable<bool>?)null, (IScheduler?)null);
	}

	public override void OnNavigatedTo(INavigationParameters parameters)
	{
		if (parameters.GetValue<string>("verified") == "true")
		{
			VerifiedMessage = "Email verified! You can now sign in.";
			IsVerified = true;
		}
		if (parameters.GetValue<string>("displaced") == "true")
		{
			DisplacedMessage = "You have been signed out because your account was signed in on another device.";
			IsDisplaced = true;
		}
	}

	private static string ToFriendlyError(string message)
	{
		string text = message.ToLowerInvariant();
		if (text.Contains("invalid login credentials") || text.Contains("invalid_credentials") || text.Contains("wrong password"))
		{
			return "Incorrect email or password. Please try again.";
		}
		if (text.Contains("email not confirmed"))
		{
			return "Please confirm your email address before signing in.";
		}
		if (text.Contains("user not found") || text.Contains("no user found"))
		{
			return "No account found with that email address.";
		}
		if (text.Contains("too many requests") || text.Contains("rate limit"))
		{
			return "Too many attempts. Please wait a moment and try again.";
		}
		if (text.Contains("network") || text.Contains("unable to connect") || text.Contains("timeout"))
		{
			return "Connection failed. Check your internet and try again.";
		}
		if (text.Contains("user already registered") || text.Contains("already exists") || text.Contains("already registered"))
		{
			return "An account with this email already exists.";
		}
		if (text.Contains("password should be at least") || text.Contains("weak_password") || text.Contains("should be at least"))
		{
			return "Password must be at least 6 characters.";
		}
		if (text.Contains("signup") && text.Contains("disabled"))
		{
			return "New account registration is currently disabled. Contact your administrator.";
		}
		if (text.Contains("invalid email") || text.Contains("unable to validate"))
		{
			return "Please enter a valid email address.";
		}
		// Return the raw message so we can see what went wrong
		return string.IsNullOrWhiteSpace(message) ? "Something went wrong. Please try again." : message;
	}
}
