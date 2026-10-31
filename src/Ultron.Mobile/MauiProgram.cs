using Microsoft.Extensions.Logging;

namespace Ultron.Mobile;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

		builder.Services.AddMauiBlazorWebView();
		// Ultron bağlantısı, ses kaydı/çalma ve fotoğraf yedeği (Faz 25).
		builder.Services.AddSingleton<Services.UltronConnection>();
		builder.Services.AddSingleton<Services.VoiceRecorder>();
		builder.Services.AddSingleton<Services.VoicePlayer>();
		builder.Services.AddSingleton<Services.ChatSession>();

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
