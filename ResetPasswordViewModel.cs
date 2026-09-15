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

public class ResetPasswordViewModel : ViewModel
{
	private readonly Supabase.Client supabase;

	private string? token;

	[Reactive]
	public string NewPassword { get; set; } = string.Empty;

	[Reactive]
	public string ConfirmPassword { get; set; } = string.Empty;

	[Reactive]
	public string ErrorMessage { get; set; } = string.Empty;

	[Reactive]
	public bool HasError { get; set; }

	public ICommand Save { get; }

	public ResetPasswordViewModel(BaseServices services, Supabase.Client supabase)
		: base(services)
	{
		this.supabase = supabase;
		this.WhenAnyValue((ResetPasswordViewModel x) => x.ErrorMessage).Subscribe(delegate(string msg)
		{
			HasError = !string.IsNullOrEmpty(msg);
		});
		Save = ReactiveCommand.CreateFromTask((Func<Task>)async delegate
		{
			if (string.IsNullOrWhiteSpace(NewPassword) || NewPassword.Length < 6)
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
						await this.supabase.Auth.SetSession(token, token);
						await this.supabase.Auth.Update(new UserAttributes
						{
							Password = NewPassword
						});
						await base.Dialogs.Alert("Password updated successfully. Please sign in.", "Done");
						await base.Navigation.NavigateAsync("/LoginPage");
						return;
					}
					catch
					{
						ErrorMessage = "Failed to reset password. The link may have expired. Please try again.";
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
		token = parameters.GetValue<string>("token");
	}

	public override void OnNavigatedFrom(INavigationParameters parameters)
	{
	}
}
