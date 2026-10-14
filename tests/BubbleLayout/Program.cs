using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GuguPet;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        LocalizationService.Initialize("zh-CN");
        var pet = new PetWindow { Opacity = 0, Topmost = false, ShowActivated = false, AutoIdleActions = false, AutoRoam = false, GazeEnabled = false };
        var sources = new[] { "codex", "dsh", "antigravity" };
        var bubbles = sources.Select(source => new StatusBubbleWindow(pet, source != "dsh", source)
            { Opacity = 0, Topmost = false, ActivityBubbleEnabled = true, DisplaySeconds = 30 }).ToArray();
        foreach (var bubble in bubbles) bubble.SetPeers(bubbles);
        try
        {
            pet.Show();
            for (var i = 0; i < bubbles.Length; i++)
            {
                var task = new CodexTaskSummary("layout-test", "多来源气泡布局测试", i == 1 ? "waiting" : "running", "", "这是一条公开回复预览。三个气泡应显示各自的来源，点击后返回对应应用。", DateTimeOffset.Now, sources[i]);
                bubbles[i].UpdateActivity(new(task.State, task.Message, task.UpdatedAt, task.ThreadId, new[] { task }, sources[i]));
            }
            Drain(app.Dispatcher);
            var area = SystemParameters.WorkArea;
            foreach (var position in new[]
            {
                new Point(area.Left + 24, area.Top + 24), new Point(area.Right - pet.Width - 24, area.Bottom - pet.Height - 24),
                new Point(area.Left + area.Width / 2, area.Top + area.Height / 2)
            })
            {
                pet.PlaceInsideWorkArea(position.X, position.Y);
                Drain(app.Dispatcher);
                var rects = bubbles.Select(b => new Rect(b.Left, b.Top, b.ActualWidth, b.ActualHeight)).ToArray();
                foreach (var rect in rects)
                    if (!area.Contains(rect)) throw new Exception("A progress bubble escaped the display work area.");
                for (var i = 0; i < rects.Length; i++)
                    for (var j = i + 1; j < rects.Length; j++)
                        if (rects[i].IntersectsWith(rects[j])) throw new Exception($"Progress bubbles overlap at pet {position}: {sources[i]}={rects[i]}, {sources[j]}={rects[j]}.");
            }
            var selectedSource = typeof(StatusBubbleWindow).GetMethod("SelectedSource", BindingFlags.Instance | BindingFlags.NonPublic)!;
            for (var i = 0; i < bubbles.Length; i++)
                if ((string)selectedSource.Invoke(bubbles[i], null)! != sources[i]) throw new Exception("Bubble return target mismatched.");
            var disabled = bubbles[2];
            disabled.ActivityBubbleEnabled = false;
            disabled.ActivityBubbleEnabled = true;
            if (!disabled.IsVisible) throw new Exception("Re-enabling a bubble did not restore the current activity.");

            var output = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(Path.GetTempPath(), "gugu-three-bubbles.png");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            var samples = bubbles.Select(bubble =>
            {
                var root = (FrameworkElement)bubble.Content;
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(root);
                return bitmap;
            }).ToArray();
            var canvas = new DrawingVisual();
            using (var drawing = canvas.RenderOpen())
            {
                drawing.DrawRectangle(Brushes.WhiteSmoke, null, new Rect(0, 0, 1260, samples.Max(s => s.PixelHeight) + 40));
                for (var i = 0; i < samples.Length; i++) drawing.DrawImage(samples[i], new Rect(10 + i * 420, 20, samples[i].Width, samples[i].Height));
            }
            var combined = new RenderTargetBitmap(1260, samples.Max(s => s.PixelHeight) + 40, 96, 96, PixelFormats.Pbgra32);
            combined.Render(canvas);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(combined));
            using var file = File.Create(output);
            encoder.Save(file);
            Console.WriteLine("PASS three-bubble-layout-and-click-targets");
            Console.WriteLine("Preview: " + output);
        }
        finally { foreach (var bubble in bubbles) bubble.Close(); pet.Close(); app.Shutdown(); }
    }

    private static void Drain(Dispatcher dispatcher)
    {
        var frame = new DispatcherFrame();
        dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
