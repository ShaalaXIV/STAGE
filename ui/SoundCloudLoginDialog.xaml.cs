using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using STAGE.Tools;

namespace STAGE;

public sealed partial class SoundCloudLoginDialog : ContentDialog
{
    private const string SignInUrl = "https://soundcloud.com/signin";
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _pollTimer;
    public bool SignedIn { get; private set; }

    public SoundCloudLoginDialog()
    {
        InitializeComponent();
        Opened += OnOpened;
        Closed += OnClosed;
    }

    public static bool IsWebViewRuntimeAvailable()
    {
        try { return !string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString()); }
        catch { return false; }
    }

    private async void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        StatusLabel.Text = "Loading SoundCloud…";
        try
        {
            var environment = await CoreWebView2Environment.CreateWithOptionsAsync(
                string.Empty, YtDlpService.WebViewDataDirectory, new CoreWebView2EnvironmentOptions());
            await LoginWebView.EnsureCoreWebView2Async(environment);
            LoginWebView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            LoginWebView.CoreWebView2.Navigate(SignInUrl);
            StatusLabel.Text = string.Empty;
            _pollTimer = DispatcherQueue.CreateTimer();
            _pollTimer.Interval = TimeSpan.FromSeconds(1);
            _pollTimer.Tick += async (_, _) => await TryCaptureTokenAsync();
            _pollTimer.Start();
        }
        catch (Exception ex) { StatusLabel.Text = "Could not open SoundCloud sign-in: " + ex.Message; }
    }

    private void OnClosed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        try
        {
            _pollTimer?.Stop();
            if (LoginWebView.CoreWebView2 != null) LoginWebView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
            LoginWebView.Close();
        }
        catch { }
    }

    private async void OnNavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args) => await TryCaptureTokenAsync();

    private async Task TryCaptureTokenAsync()
    {
        if (SignedIn || LoginWebView.CoreWebView2 == null) return;
        try
        {
            var cookies = await LoginWebView.CoreWebView2.CookieManager.GetCookiesAsync("https://soundcloud.com");
            var token = cookies.FirstOrDefault(c => c.Name == "oauth_token");
            if (token == null || string.IsNullOrWhiteSpace(token.Value) || token.Value.Length < 20 || !token.Value.Contains('-')) return;
            Settings.SoundCloudToken = token.Value;
            SignedIn = true;
            _pollTimer?.Stop();
            Hide();
        }
        catch { }
    }
}
