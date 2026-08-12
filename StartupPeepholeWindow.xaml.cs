using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace GuguPet;

public partial class StartupPeepholeWindow : Window
{
    private static readonly int[] HoldMilliseconds = { 420, 480, 260, 500, 470, 520, 720, 620 };
    private readonly BitmapSource[] _frames;
    private bool _finishing;

    public event EventHandler? Completed;

    public StartupPeepholeWindow()
    {
        InitializeComponent();
        LocalizationService.Apply(this);
        _frames = LoadFrames();
        Opacity = 0;
        Loaded += async (_, _) => await PlayAsync();
    }

    private static BitmapSource[] LoadFrames()
    {
        var sheet = new BitmapImage();
        sheet.BeginInit();
        sheet.CacheOption = BitmapCacheOption.OnLoad;
        sheet.UriSource = new Uri("pack://application:,,,/Assets/startup-peephole.png", UriKind.Absolute);
        sheet.EndInit();
        sheet.Freeze();

        if (sheet.PixelWidth % 4 != 0 || sheet.PixelHeight % 2 != 0)
            throw new InvalidDataException("Startup peephole atlas must be a 4x2 grid.");

        var width = sheet.PixelWidth / 4;
        var height = sheet.PixelHeight / 2;
        var frames = new BitmapSource[8];
        for (var index = 0; index < frames.Length; index++)
        {
            var crop = new CroppedBitmap(sheet, new Int32Rect(
                index % 4 * width,
                index / 4 * height,
                width,
                height));
            crop.Freeze();
            frames[index] = crop;
        }
        return frames;
    }

    private async Task PlayAsync()
    {
        FrameA.Source = _frames[0];
        await AnimateOpacityAsync(this, 0, 1, 220);

        for (var index = 0; index < _frames.Length && !_finishing; index++)
        {
            await Task.Delay(HoldMilliseconds[index]);
            if (_finishing || index == _frames.Length - 1) break;
            FrameB.Source = _frames[index + 1];
            FrameB.Opacity = 0;
            await AnimateOpacityAsync(FrameB, 0, 1, 60);
            FrameA.Source = _frames[index + 1];
            FrameB.BeginAnimation(OpacityProperty, null);
            FrameB.Opacity = 0;
        }

        if (_finishing) return;
        await AnimateOpacityAsync(this, 1, 0, 320);
        Finish();
    }

    private static Task AnimateOpacityAsync(UIElement target, double from, double to, int milliseconds)
    {
        var completion = new TaskCompletionSource();
        var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds))
        {
            FillBehavior = FillBehavior.Stop,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };
        animation.Completed += (_, _) =>
        {
            target.BeginAnimation(OpacityProperty, null);
            target.Opacity = to;
            completion.TrySetResult();
        };
        target.BeginAnimation(OpacityProperty, animation);
        return completion.Task;
    }

    private void Window_OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Finish();
    }

    private void Finish()
    {
        if (_finishing) return;
        _finishing = true;
        Completed?.Invoke(this, EventArgs.Empty);
        Close();
    }
}
