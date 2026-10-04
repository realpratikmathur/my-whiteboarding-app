using System.Threading.Tasks;

namespace InkBoard;

// Recording and replay (Phase 11). Until then the page has no recording files.
public sealed partial class MainWindow
{
    private partial Task SaveRecordingAsync(string pageFolder) => Task.CompletedTask;

    private partial Task LoadRecordingAsync(string pageFolder) => Task.CompletedTask;
}
