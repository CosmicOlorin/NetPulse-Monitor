using ZXing.Net.Maui;
using ZXing.Net.Maui.Controls;

namespace NetPulse.Companion.App;

public sealed class QrScannerPage : ContentPage
{
    private readonly TaskCompletionSource<string?> _completion = new();
    private readonly CameraBarcodeReaderView _camera;

    public QrScannerPage()
    {
        Title = "Scan pairing QR";
        BackgroundColor = Color.FromArgb("#111820");
        _camera = new CameraBarcodeReaderView
        {
            Options = new BarcodeReaderOptions { Formats = BarcodeFormats.TwoDimensional, AutoRotate = true, Multiple = false },
            HorizontalOptions = LayoutOptions.Fill, VerticalOptions = LayoutOptions.Fill
        };
        _camera.BarcodesDetected += OnBarcodesDetected;
        var cancel = new Button { Text = "Cancel", BackgroundColor = Color.FromArgb("#283746"), TextColor = Colors.White, Margin = 24, VerticalOptions = LayoutOptions.End };
        cancel.Clicked += async (_, _) => await FinishAsync(null);
        Content = new Grid
        {
            Children =
            {
                _camera,
                new Border { Stroke = Color.FromArgb("#63D9A7"), StrokeThickness = 3, BackgroundColor = Colors.Transparent, Margin = 50, HorizontalOptions = LayoutOptions.Fill, VerticalOptions = LayoutOptions.Center, HeightRequest = 260 },
                cancel
            }
        };
    }

    public async Task<string?> ScanAsync(INavigation navigation) { await navigation.PushModalAsync(new NavigationPage(this)); return await _completion.Task; }
    private void OnBarcodesDetected(object? sender, BarcodeDetectionEventArgs e)
    {
        string? value = e.Results.FirstOrDefault()?.Value;
        if (!string.IsNullOrWhiteSpace(value)) MainThread.BeginInvokeOnMainThread(async () => await FinishAsync(value));
    }
    private async Task FinishAsync(string? value)
    {
        if (_completion.Task.IsCompleted) return;
        _camera.IsDetecting = false; _completion.TrySetResult(value); await Navigation.PopModalAsync();
    }
}
