using System;
using System.Reactive.Concurrency;
using System.Threading.Tasks;
using System.Windows.Input;
using Prism.Navigation;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Shiny;
using Supabase;
using Supabase.Gotrue;

namespace GpsSync;

public class ForgotPasswordViewModel : ViewModel
{
	private readonly Supabase.Client supabase;

	[Reactive]
	public string Email { get; set; } = string.Empty;

	[Reactive]
	public string Code { get; set; } = string.Empty;

	[Reactive]
	public string NewPassword { get; set; } = string.Empty;

	[Reactive]
	public string ConfirmPassword { get; set; } = string.Empty;

	[Reactive]
	public string ErrorMessage { get; set; } = string.Empty;

	[Reactive]
	public string SuccessMessage { get; set; } = string.Empty;

	[Reactive]
	public bool HasError { get; set; }

	[Reactive]
	public bool CodeSent { get; set; }

	public ICommand SendCode { get; }

	public ICommand ResetPassword { get; }

	public ICommand NavigateBack => ReactiveCommand.CreateFromTask(() => base.Navigation.GoBackAsync());

	public ForgotPasswordViewModel(BaseServices services, Supabase.Client supabase)
		: base(services)
	{
		this.supabase = supabase;
		this.WhenAnyValue((ForgotPasswordViewModel x) => x.ErrorMessage).Subscribe(delegate(string msg)
		{
			HasError = !string.IsNullOrEmpty(msg);
		});
		SendCode = ReactiveCommand.CreateFromTask((Func<Task>)async delegate
		{
			if (string.IsNullOrWhiteSpace(Email))
			{
				ErrorMessage = "Enter your email address.";
				return;
			}
			ErrorMessage = string.Empty;
			base.IsBusy = true;
			try
			{
				await this.supabase.Auth.ResetPasswordForEmail(new ResetPasswordForEmailOptions(Email.Trim()));
				CodeSent = true;
				SuccessMessage = "Code sent! Check your email for the 6-digit code.";
			}
			catch
			{
				ErrorMessage = "Failed to send code. Check your email address and try again.";
			}
			finally
			{
				base.IsBusy = false;
			}
		}, (IObservable<bool>?)null, (IScheduler?)null);
		ResetPassword = ReactiveCommand.CreateFromTask((Func<Task>)async delegate
		{
			if (string.IsNullOrWhiteSpace(Code))
			{
				ErrorMessage = "Enter the code from your email.";
			}
			else if (string.IsNullOrWhiteSpace(NewPassword) || NewPassword.Length < 6)
			{
				ErrorMessage = "Password must be at least 6 characters.";
			}
			else
			{
				if (!(NewPassword != ConfirmPassword))
				{
					ErrorMessage = string.Empty;
					base.IsBusy = true;
					try
					{
						if ((await this.supabase.Auth.VerifyOTP(Email.Trim(), Code.Trim(), Constants.EmailOtpType.Recovery))?.User == null)
						{
							ErrorMessage = "Invalid or expired code. Please request a new one.";
						}
						else
						{
							await this.supabase.Auth.Update(new UserAttributes
							{
								Password = NewPassword
							});
							await base.Dialogs.Alert("Password updated successfully! Please sign in with your new password.", "Done");
							await base.Navigation.NavigateAsync("/LoginPage");
						}
						return;
					}
					catch
					{
						ErrorMessage = "Invalid or expired code. Please request a new one.";
						return;
					}
					finally
					{
						base.IsBusy = false;
					}
				}
				ErrorMessage = "Passwords do not match.";
			}
		}, (IObservable<bool>?)null, (IScheduler?)null);
	}

	public override void OnNavigatedTo(INavigationParameters parameters)
	{
		string value = parameters.GetValue<string>("email");
		if (!string.IsNullOrEmpty(value))
		{
			Email = value;
		}
	}

	public override void OnNavigatedFrom(INavigationParameters parameters)
	{
	}
}
