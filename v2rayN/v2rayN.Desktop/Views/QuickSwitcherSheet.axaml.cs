using DialogHostAvalonia;

namespace v2rayN.Desktop.Views;

/// <summary>
/// Ctrl+K: jump to a server (recently used ones first) or run an everyday action by typing a few
/// letters. Up / Down choose, Enter runs, Escape closes.
/// </summary>
public partial class QuickSwitcherSheet : UserControl
{
    public sealed record Entry(string Title, string Subtitle, Func<Task> Run);

    private const int MaxResults = 12;
    private List<Entry> _servers = [];
    private IReadOnlyList<Entry> _actions = [];

    public QuickSwitcherSheet()
    {
        InitializeComponent();
        txtQuery.TextChanged += (_, _) => Filter();
        txtQuery.KeyDown += async (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Down:
                    lstResults.SelectedIndex = Math.Min(lstResults.SelectedIndex + 1, lstResults.ItemCount - 1);
                    e.Handled = true;
                    break;
                case Key.Up:
                    lstResults.SelectedIndex = Math.Max(lstResults.SelectedIndex - 1, 0);
                    e.Handled = true;
                    break;
                case Key.Enter:
                    e.Handled = true;
                    await RunSelectedAsync();
                    break;
                case Key.Escape:
                    e.Handled = true;
                    Close();
                    break;
            }
        };
        lstResults.DoubleTapped += async (_, _) => await RunSelectedAsync();
    }

    /// <summary>Loads servers and the actions, clears the query and focuses it.</summary>
    public async Task ShowAsync(MainWindowViewModel main, IReadOnlyList<Entry> actions)
    {
        _actions = actions;
        var recent = RecentStore.All();
        var profiles = await AppManager.Instance.ProfileItems("") ?? [];
        _servers = profiles
            .OrderBy(p => recent.ToList().IndexOf(p.IndexId) is var i && i >= 0 ? i : int.MaxValue)
            .Select(p => new Entry(
                p.Remarks ?? p.IndexId,
                Converters.ServerLineConverter.TypeName(p.ConfigType.ToString(), p.Remarks),
                () => main.ProfilesViewModel.SetDefaultServer(p.IndexId)))
            .ToList();
        txtQuery.Text = string.Empty;
        Filter();
        Dispatcher.UIThread.Post(() => txtQuery.Focus(), DispatcherPriority.Loaded);
    }

    private void Filter()
    {
        var query = txtQuery.Text?.Trim() ?? string.Empty;
        bool Matches(Entry e) => query.IsNullOrEmpty() || e.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase);
        // Typing: servers first (that's what people look for); empty: recent servers, then the actions.
        var results = query.IsNullOrEmpty()
            ? _servers.Take(5).Concat(_actions)
            : _servers.Where(Matches).Concat(_actions.Where(Matches));
        lstResults.ItemsSource = results.Take(MaxResults).ToList();
        lstResults.SelectedIndex = lstResults.ItemCount > 0 ? 0 : -1;
    }

    private async Task RunSelectedAsync()
    {
        if (lstResults.SelectedItem is not Entry entry)
        {
            return;
        }
        Close();
        try
        {
            await entry.Run();
        }
        catch (Exception ex)
        {
            // An exception escaping here would reach the dispatcher and close the app.
            Logging.SaveLog("QuickSwitcher", ex);
        }
    }

    private static void Close()
    {
        if (DialogHost.IsDialogOpen(null))
        {
            DialogHost.Close(null);
        }
    }
}
